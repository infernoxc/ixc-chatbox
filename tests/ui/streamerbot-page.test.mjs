// The Streamer.bot page inside the IXC dashboard: reachable from the navigation, calls the real backend, persists, never shows the password.
// MOCK Streamer.bot (fake WebSocket server); no real Streamer.bot, account or platform is involved.
import { test, before, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { chromium } from 'playwright';
import { startCore, api, fakeStreamerBot, until } from '../integration/harness.mjs';
const exe = process.env.IXC_BROWSER || (fs.existsSync('/opt/pw-browsers/chromium') ? '/opt/pw-browsers/chromium' : undefined);
const PW = 'sb-Secret-Pass-12345', NOSB = path.join(os.tmpdir(), 'ixc-no-streamerbot', 'settings.json');
let c, b, sb;
before(async () => { sb = await fakeStreamerBot(); c = await startCore({ env: { IXC_EP_STREAMERBOT: sb.url, IXC_EP_SB_EXE: 'IxcNoSuch.exe' }, config: { platforms: { streamerbot: { mode: 'on', settingsPath: NOSB } } } }); b = await chromium.launch({ executablePath: exe, args: ['--no-sandbox'] }); });
after(async () => { await b?.close(); await c?.stop(); await sb?.close(); });

test('MOCK Streamer.bot page: in the navigation, saves through the backend, connects, tests, persists, no password on the page', async () => {
  const p = await b.newPage({ viewport: { width: 1200, height: 1000 } }); await p.goto(`http://127.0.0.1:${c.port}/app/`);
  await p.waitForSelector('#navList a[data-p="streamerbot"]'); assert.match(await p.locator('#navList a[data-p="streamerbot"]').innerText(), /Streamer\.bot/);
  await p.click('#navList a[data-p="streamerbot"]'); await p.waitForSelector('#sbForm #sbUrl'); await p.waitForSelector('#sbStatus .kv');
  assert.match(await p.locator('#sbStatus').innerText(), /Password[\s\S]*none/);   // nothing saved yet
  // wrong input: refused, entries stay
  await p.fill('#sbUrl', 'ws://example.com:8080/'); await p.fill('#sbPw', PW); await p.click('[data-sb="save"]'); await p.waitForFunction(() => /Not saved/.test(document.getElementById('sbMsg').textContent));
  assert.match(await p.locator('#sbMsg').innerText(), /only connects to Streamer.bot on this PC/); assert.equal(await p.inputValue('#sbUrl'), 'ws://example.com:8080/'); assert.equal(await p.inputValue('#sbPw'), PW);
  // good input
  await p.fill('#sbUrl', 'ws://127.0.0.1:8080/'); await p.click('[data-sb="save"]'); await p.waitForFunction(() => /^Saved/.test(document.getElementById('sbMsg').textContent));
  assert.equal(await p.inputValue('#sbPw'), '', 'password box emptied after a confirmed save'); assert.equal(await p.locator('#sbPwSaved').isVisible(), true);
  assert.ok(!(await p.content()).includes(PW) && !(await p.evaluate(() => document.body.innerText)).includes(PW));
  // the backend really connected to the fake Streamer.bot (not just the UI saying so)
  await until(async () => (await api(c, '/api/streamerbot')).body.link.ready, 20000, 'backend connected'); assert.ok(sb.srv.live().length >= 1);
  await p.waitForFunction(() => /Connected/.test(document.getElementById('sbStatus').innerText), null, { timeout: 15000 });
  await p.click('[data-sb="test"]'); await p.waitForFunction(() => /Connected to Streamer\.bot 1\.0\.7/.test(document.getElementById('sbTest').textContent), null, { timeout: 15000 });
  assert.match(await p.locator('#sbTest').innerText(), /password accepted. Accounts: .*mykick/);
  // persistence: reload the page, then restart IXC itself
  await p.reload(); await p.waitForSelector('#sbForm #sbPw'); assert.equal(await p.inputValue('#sbPw'), ''); assert.equal(await p.locator('#sbPwSaved').isVisible(), true);
  c = await c.restart(); const q = await b.newPage(); await q.goto(`http://127.0.0.1:${c.port}/app/#streamerbot`); await q.waitForSelector('#sbForm #sbUrl'); await q.waitForFunction(() => document.getElementById('sbPwSaved') && !document.getElementById('sbPwSaved').hidden);
  assert.equal(await q.inputValue('#sbUrl'), 'ws://127.0.0.1:8080/'); await until(async () => (await api(c, '/api/streamerbot')).body.link.ready, 20000, 'reconnected after the IXC restart');
  assert.ok(!(fs.readFileSync(path.join(c.dir, 'config.json'), 'utf8') + c.output() + fs.readdirSync(path.join(c.dir, 'logs')).map(f => fs.readFileSync(path.join(c.dir, 'logs', f), 'utf8')).join('')).includes(PW), 'password never in config or logs');
  // wrong password through the UI: a clear message
  await q.fill('#sbPw', 'not-the-password-123'); await q.click('[data-sb="save"]'); await q.waitForFunction(() => /^Saved/.test(document.getElementById('sbMsg').textContent));
  await q.click('[data-sb="test"]'); await q.waitForFunction(() => /refused the password/.test(document.getElementById('sbTest').textContent), null, { timeout: 15000 });
});
