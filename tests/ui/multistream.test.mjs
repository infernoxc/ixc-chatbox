// Multistream page in a real browser: add / edit / delete destinations, keys never shown again, the 8-destination limit, input errors,
// relay start / stop, and honest "server reachable" vs "publishing" status against fake RTMP servers (rtmp.mjs). Screenshots: tests/ui/shots/.
// Uses Chromium if Playwright has one, otherwise the Edge that ships with Windows.
import { test, before, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { startCore, api, until, freePort, sleep } from '../integration/harness.mjs'; import { fakeDestination, fakePublisher } from '../integration/rtmp.mjs';

const shots = path.join(path.dirname(fileURLToPath(import.meta.url)), 'shots'); fs.mkdirSync(shots, { recursive: true });
const KEY = 'live_55555_UiSecretStreamKey';
let c, b, ingestPort; const errors = []; const cleanup = [];
before(async () => { ingestPort = await freePort(); c = await startCore({ env: { IXC_EP_MULTISTREAM_INGEST_PORT: String(ingestPort), IXC_EP_MULTISTREAM_RETRY_MS: '150', IXC_EP_MULTISTREAM_BACKOFF_MAX_MS: '600', IXC_EP_MULTISTREAM_TIMEOUT_MS: '3000' }, config: { general: { firstRunDone: true } } });
  const exe = fs.existsSync('/opt/pw-browsers/chromium') ? '/opt/pw-browsers/chromium' : undefined;
  try { b = await chromium.launch({ executablePath: exe, args: ['--no-sandbox'] }); } catch { b = await chromium.launch({ executablePath: edge(), args: ['--no-sandbox'] }); } });
// Windows without Playwright's Chromium: the Edge that Windows installs (set IXC_TEST_BROWSER to use another browser)
function edge() { if (process.env.IXC_TEST_BROWSER) return process.env.IXC_TEST_BROWSER; const base = 'C:/Program Files (x86)/Microsoft/EdgeCore'; for (const v of fs.existsSync(base) ? fs.readdirSync(base).sort().reverse() : []) { const f = path.join(base, v, 'msedge.exe'); if (fs.existsSync(f)) return f; } return undefined; }
after(async () => { for (const f of cleanup.reverse()) await f(); await b?.close(); await c?.stop(); });
async function open() { const p = await b.newPage({ viewport: { width: 1280, height: 1000 } }); p.on('pageerror', e => errors.push(e.message)); p.on('console', m => { if (m.type() === 'error' && !/Failed to load resource|net::|ERR_|fonts/i.test(m.text())) errors.push(m.text()); });
  await p.goto(c.url.replace('127.0.0.1', 'localhost') + '/app/#multistream-legacy'); await p.waitForSelector('#msAdd'); return p; }
const shot = (p, n) => p.screenshot({ path: path.join(shots, 'ms-' + n + '.png') });
const list = async () => (await api(c, '/api/multistream')).body;
async function addVia(p, { platform = 'custom', name, url, key }) { await p.click('#msAdd'); await p.selectOption('#mP', platform); await p.fill('#mN', name); await p.fill('#mU', url); await p.fill('#mK', key); await p.click('#mSave'); }

test('multistream page: add, edit, hide the key, error on a bad address, delete', async () => {
  const p = await open(); await shot(p, '01-empty');
  assert.match(await p.textContent('main, #main'), /Relay/); assert.equal(await p.locator('#msStart').count(), 1); assert.match(await p.textContent('#main'), /No destinations yet/);
  // bad address -> error toast, nothing saved
  await p.click('#msAdd'); await p.fill('#mN', 'Bad'); await p.fill('#mU', 'http://example.com/live'); await p.fill('#mK', KEY); await p.click('#mSave'); await p.waitForSelector('text=rtmp://'); assert.equal((await list()).destinations.length, 0);
  assert.equal(await p.inputValue('#mK'), '', 'the key box is emptied after a failed save too'); await p.click('[data-close]');
  // a good one
  await addVia(p, { name: 'My test server', url: 'rtmp://127.0.0.1:1/live', key: KEY }); await p.waitForSelector('.card[data-id]'); await shot(p, '02-one-destination');
  const text = await p.textContent('#main'); assert.match(text, /My test server/); assert.match(text, /Key saved - hidden/); assert.ok(!text.includes(KEY), 'the key is not on the page'); assert.ok(!(await p.content()).includes(KEY), 'nor in the page source');
  const api1 = JSON.stringify((await list())); assert.ok(!api1.includes(KEY), 'nor in GET /api/multistream'); assert.equal((await list()).destinations[0].keySet, true);
  // edit: the key field is empty and says it is saved; renaming keeps the key
  await p.click('[data-act=edit]'); assert.equal(await p.inputValue('#mK'), ''); assert.match(await p.getAttribute('#mK', 'placeholder'), /saved/); assert.equal(await p.getAttribute('#mK', 'type'), 'password');
  await p.fill('#mN', 'Renamed'); await p.click('#mSave'); await until(async () => (await list()).destinations[0].name === 'Renamed', 5000, 'renamed'); assert.equal((await list()).destinations[0].keySet, true, 'key kept');
  // switch off / on
  await p.waitForSelector('.card[data-id]'); await p.click('.card[data-id] .switch'); await until(async () => (await list()).destinations[0].enabled === false, 5000, 'turned off'); await p.click('.card[data-id] .switch'); await until(async () => (await list()).destinations[0].enabled === true, 5000, 'turned on');
  // remove the saved key
  await p.click('[data-act=edit]'); await p.check('#mC'); await p.click('#mSave'); await until(async () => (await list()).destinations[0].keySet === false, 5000, 'key removed'); await p.waitForSelector('text=No stream key yet');
  // delete asks first
  await p.click('[data-act=del]'); await p.waitForSelector('.modal.show'); await p.click('.modal.show button.primary, .modal.show button.danger, .modal.show button:last-child'); await until(async () => (await list()).destinations.length === 0, 5000, 'deleted');
  await p.close(); assert.deepEqual(errors, []);
});

test('multistream page: the eight-destination limit is shown and enforced', async () => {
  for (let i = 0; i < 8; i++) await api(c, '/api/multistream/save', { platform: 'custom', name: 'D' + i, url: 'rtmp://127.0.0.1:1/x' + i, key: KEY + i });
  const p = await open(); await p.waitForSelector('.card[data-id]'); assert.equal(await p.locator('.card[data-id]').count(), 8); assert.equal(await p.isDisabled('#msAdd'), true); assert.match(await p.textContent('#main'), /limit is 8/); await shot(p, '03-eight');
  assert.equal((await api(c, '/api/multistream/save', { platform: 'custom', url: 'rtmp://127.0.0.1:1/ninth' })).status, 400);
  for (const d of (await list()).destinations) await api(c, '/api/multistream/delete', { id: d.id });
  await p.close(); assert.deepEqual(errors, []);
});

test('multistream page: relay start/stop, "server reachable" is not "publishing", a rejecting server shows its reason', async () => {
  const ok = await fakeDestination({}), bad = await fakeDestination({ reject: 'NetStream.Publish.BadName' }); cleanup.push(ok.close, bad.close);
  await api(c, '/api/multistream/save', { platform: 'custom', name: 'Accepts', url: ok.url, key: KEY }); await api(c, '/api/multistream/save', { platform: 'custom', name: 'Rejects', url: bad.url, key: KEY });
  const p = await open(); await p.waitForSelector('.card[data-id]');
  assert.match(await p.textContent('#main'), /Not publishing \(the relay is off\)/); assert.match(await p.textContent('#main'), /Server not checked/);
  // "Check server": reachable, and the page says that is not proof the key works
  await p.click('.card[data-id]:has-text("Accepts") [data-act=check]'); await p.waitForSelector('.card:has-text("Accepts"):has-text("Server reachable")'); assert.match(await p.textContent('.card:has-text("Accepts")'), /does not prove the key is accepted/); assert.match(await p.textContent('.card:has-text("Accepts")'), /Not publishing/);
  // start the relay: ingest address shown, key hidden until asked
  await p.click('#msStart'); await p.waitForSelector('#msSrv'); assert.equal(await p.inputValue('#msSrv'), 'rtmp://127.0.0.1:' + ingestPort + '/live'); assert.equal(await p.getAttribute('#msKey', 'type'), 'password');
  const token = await p.inputValue('#msKey'); assert.match(token, /^ixc-/); await p.click('[data-act=showkey]'); assert.equal(await p.getAttribute('#msKey', 'type'), 'text'); assert.match(await p.textContent('#main'), /waiting for OBS/); await shot(p, '04-relay-waiting');
  // OBS (fake) starts: the accepting server is publishing, the rejecting one says why
  const pub = await fakePublisher(ingestPort, token); await pub.ready; pub.meta(); pub.audioSeq(); pub.videoSeq();
  await p.waitForSelector('.card:has-text("Accepts"):has-text("Publishing - the server accepted")'); await p.waitForSelector('.card:has-text("Rejects"):has-text("NetStream.Publish.BadName")'); assert.match(await p.textContent('#main'), /OBS is sending/);
  for (let i = 0; i < 20; i++) { pub.video(i * 33, i % 10 === 0); pub.audio(i * 33); await sleep(5); } await shot(p, '05-publishing');
  assert.ok(!(await p.content()).includes(KEY)); assert.ok(!JSON.stringify(await list()).includes(KEY));
  pub.end(); await p.waitForSelector('text=waiting for OBS'); await p.click('#msStop'); await p.waitForSelector('#msStart'); assert.match(await p.textContent('#main'), /Off/);
  await assert.rejects(fakePublisher(ingestPort, token).then(x => x.ready), 'stopping the relay closes the ingest');
  // starting when the port is taken shows the real reason
  const hold = (await import('node:net')).createServer(); await new Promise(r => hold.listen(ingestPort, '127.0.0.1', r)); await p.click('#msStart'); await p.waitForSelector('text=another program'); await new Promise(r => hold.close(r));
  for (const d of (await list()).destinations) await api(c, '/api/multistream/delete', { id: d.id });
  await p.close(); assert.deepEqual(errors, []);
});
