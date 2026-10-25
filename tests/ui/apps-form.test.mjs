// Platforms & Accounts > Your apps: typed values survive status refreshes, Save is confirmed from the backend, failures keep the
// entries and say why, secrets are write-only. Fake values only; no screenshots are taken (the page holds a typed secret).
import { test, before, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs';
import { chromium } from 'playwright';
import { startCore, api, until, sleep } from '../integration/harness.mjs';
const exe = process.env.IXC_BROWSER || (fs.existsSync('/opt/pw-browsers/chromium') ? '/opt/pw-browsers/chromium' : undefined);
const KICK_ID = '01ABCDEFGHJKMNPQRS', KICK_SECRET = 'kick-TopSecret-1234567890', TW_ID = 'abcdefghij0123456789abcd';
let c, b;
before(async () => { c = await startCore({ config: { general: { advanced: true } } });   // Your apps is an Advanced-mode card (Streamer.bot is the normal way) b = await chromium.launch({ executablePath: exe, args: ['--no-sandbox'] }); });
after(async () => { await b?.close(); await c?.stop(); });
const open = async () => { const p = await b.newPage({ viewport: { width: 1100, height: 1400 } }); await p.goto(`http://127.0.0.1:${c.port}/app/#accounts`); await p.waitForSelector('#appsBox input'); for (const d of await p.locator('#appsBox details').all()) await d.evaluate(e => { e.open = true; }); return p; };

test('typed Client ID / secret survive status refreshes and account updates (the blank-fields bug)', async () => {
  const p = await open();
  await p.fill('#app_kick_id', KICK_ID); await p.fill('#app_kick_secret', KICK_SECRET); await p.fill('#app_twitch_id', TW_ID);
  // things that make the core publish "status" and "accounts" to the open page
  await api(c, '/api/settings', { patch: { 'platforms.twitch.channel': 'someone' } }); await api(c, '/api/settings', { patch: { 'platforms.twitch.enabled': true } }); await api(c, '/api/repair', { action: 'platform.twitch' });
  await sleep(2500);
  assert.equal(await p.inputValue('#app_kick_id'), KICK_ID); assert.equal(await p.inputValue('#app_kick_secret'), KICK_SECRET); assert.equal(await p.inputValue('#app_twitch_id'), TW_ID);
  // saving another platform in the background changes the saved data: the card is redrawn but what is typed stays
  await api(c, '/api/accounts/apps', { google: { id: '123456789012-abcdefghijklmnop.apps.googleusercontent.com', secret: 'GOCSPX-TopSecretValue12345' } }); await sleep(1500);
  assert.equal(await p.inputValue('#app_kick_id'), KICK_ID); assert.equal(await p.inputValue('#app_kick_secret'), KICK_SECRET);
  await p.close();
});

test('Save: confirmed from the backend, ID shown after reload, secret write-only with a "Secret saved" indicator', async () => {
  const p = await open(); await p.fill('#app_kick_id', KICK_ID); await p.fill('#app_kick_secret', KICK_SECRET);
  await p.click('[data-app-save="kick"]'); await p.waitForFunction(() => /Saved/.test(document.getElementById('app_kick_msg').textContent));
  assert.equal(await p.inputValue('#app_kick_id'), KICK_ID); assert.equal(await p.inputValue('#app_kick_secret'), '', 'the secret box is emptied after a confirmed save');
  assert.match(await p.locator('#app_kick_saved').innerText(), /Secret saved/);
  assert.ok(!(await p.content()).includes(KICK_SECRET) && !(await p.evaluate(() => document.body.innerText)).includes(KICK_SECRET), 'the secret is nowhere on the page');
  await p.reload(); await p.waitForSelector('#appsBox input'); for (const d of await p.locator('#appsBox details').all()) await d.evaluate(e => { e.open = true; });
  assert.equal(await p.inputValue('#app_kick_id'), KICK_ID, 'ID comes back after a reload'); assert.match(await p.locator('#app_kick_saved').innerText(), /Secret saved/);
  assert.equal(await p.getAttribute('#app_kick_secret', 'placeholder'), '(saved - paste a new one to replace it)');
  assert.equal((await api(c, '/api/accounts')).body.apps.kick.id, KICK_ID);
  await p.close();
});

test('failures keep what was typed and say why: invalid input, backend refusal, no answer; an empty box never wipes a saved ID', async () => {
  const p = await open(); await p.fill('#app_kick_id', 'not valid!!'); await p.fill('#app_kick_secret', 'typed-secret-0987654321');
  await p.click('[data-app-save="kick"]'); await p.waitForFunction(() => /Not saved/.test(document.getElementById('app_kick_msg').textContent));
  assert.match(await p.locator('#app_kick_msg').innerText(), /Kick Client ID/); assert.equal(await p.inputValue('#app_kick_id'), 'not valid!!'); assert.equal(await p.inputValue('#app_kick_secret'), 'typed-secret-0987654321');
  assert.equal((await api(c, '/api/accounts')).body.apps.kick.id, KICK_ID, 'the saved ID is untouched');
  // backend failure (HTTP 500 with a reason)
  await p.fill('#app_kick_id', KICK_ID);
  await p.route('**/api/accounts/apps', (r) => r.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ ok: false, error: 'the disk is full' }) }));
  await p.click('[data-app-save="kick"]'); await p.waitForFunction(() => /disk is full/.test(document.getElementById('app_kick_msg').textContent));
  assert.equal(await p.inputValue('#app_kick_secret'), 'typed-secret-0987654321', 'entries kept on a backend failure');
  // IXC not answering at all
  await p.unroute('**/api/accounts/apps'); await p.route('**/api/accounts/apps', (r) => r.abort());
  await p.click('[data-app-save="kick"]'); await p.waitForFunction(() => /Not saved/.test(document.getElementById('app_kick_msg').textContent) && !/disk is full/.test(document.getElementById('app_kick_msg').textContent));
  assert.equal(await p.inputValue('#app_kick_secret'), 'typed-secret-0987654321'); await p.unroute('**/api/accounts/apps');
  // empty fields: nothing is sent, the saved ID stays
  await p.fill('#app_kick_id', ''); await p.fill('#app_kick_secret', ''); await p.click('[data-app-save="kick"]');
  await p.waitForFunction(() => /Nothing to save/.test(document.getElementById('app_kick_msg').textContent));
  assert.equal((await api(c, '/api/accounts')).body.apps.kick.id, KICK_ID, 'an empty box does not clear the saved ID');
  await p.close();
});
