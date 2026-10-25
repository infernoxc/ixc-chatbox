// Stage 1 dashboard: the navigation structure and the real page shells (Overview, Chat, Platforms & Accounts, Multistream Studio, Vertical Studio,
// Automation Studio, OBS Control, Settings). Fake OBS here; the same pages were also checked by hand against the real running IXC and OBS.
// Rules checked: every nav entry opens a page without script errors, pages show what the backend really reports, and what is not built says so
// instead of offering a control. Screenshots: tests/ui/shots/.
import { test, before, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { startCore, api, settings, fakeObs, until } from '../integration/harness.mjs';

const shots = path.join(path.dirname(fileURLToPath(import.meta.url)), 'shots'); fs.mkdirSync(shots, { recursive: true });
let c, c2, b, obs; const errors = [];
function edge() { if (process.env.IXC_TEST_BROWSER) return process.env.IXC_TEST_BROWSER; const base = 'C:/Program Files (x86)/Microsoft/EdgeCore'; for (const v of fs.existsSync(base) ? fs.readdirSync(base).sort().reverse() : []) { const f = path.join(base, v, 'msedge.exe'); if (fs.existsSync(f)) return f; } return undefined; }
before(async () => { obs = await fakeObs(); c = await startCore({ config: { general: { firstRunDone: true }, obs: { websocketUrl: obs.url } } }); c2 = await startCore({ config: { general: { firstRunDone: true } } });   // c2: no OBS
  const exe = fs.existsSync('/opt/pw-browsers/chromium') ? '/opt/pw-browsers/chromium' : undefined;
  try { b = await chromium.launch({ executablePath: exe, args: ['--no-sandbox'] }); } catch { b = await chromium.launch({ executablePath: edge(), args: ['--no-sandbox'] }); }
  await until(async () => (await api(c, '/api/obs/state')).body.connected, 10000, 'fake OBS connected'); });
after(async () => { await b?.close(); await c?.stop(); await c2?.stop(); await obs?.close(); });
async function open(core, hash) { const p = await b.newPage({ viewport: { width: 1280, height: 1000 } }); p.on('pageerror', e => errors.push(hash + ': ' + e.message)); p.on('console', m => { if (m.type() === 'error' && !/Failed to load resource|net::|ERR_|fonts|youtube|jtvnw|kick\.com/i.test(m.text())) errors.push(hash + ': ' + m.text()); });
  await p.goto(core.url.replace('127.0.0.1', 'localhost') + '/app/#' + hash); await p.waitForSelector('#navList a'); await p.waitForFunction(() => document.querySelector('#main').children.length > 0 || document.querySelector('#main').textContent.trim().length > 0); return p; }
const shot = (p, n) => p.screenshot({ path: path.join(shots, 'stage1-' + n + '.png') });
const text = (p) => p.textContent('#main').then(t => t.replace(/\s+/g, ' '));

test('navigation: the eight Stage 1 pages in order, every existing feature still reachable, every entry opens without errors', async () => {
  const p = await open(c, 'home'); const nav = (await p.$$eval('#navList a', a => a.map(x => [...x.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent).join('').trim())));
  const want = ['Overview', 'Chat', 'Platforms & Accounts', 'Automation Studio', 'OBS Control', 'Settings']; let at = -1; for (const w of want) { const i = nav.indexOf(w, at + 1); assert.ok(i > at, w + ' missing or out of order in ' + nav.join(' | ')); at = i; }
  for (const keep of ['Commands', 'Timers', 'Chat filters', 'Music', 'Song requests', 'Audio routing', 'Chat voice (TTS)', 'Voices for people', 'Overlays & docks', 'Phone remote', 'Updates & diagnostics']) assert.ok(nav.includes(keep), 'existing page lost from the navigation: ' + keep);
  const ids = await p.$$eval('#navList a', a => a.map(x => x.dataset.p)); assert.equal(new Set(ids).size, ids.length, 'no duplicate entries'); await shot(p, '01-nav');
  for (const id of ids) { await p.evaluate((h) => { location.hash = '#' + h; }, id); await p.waitForTimeout(350); assert.equal(await p.evaluate(() => document.querySelector('#navList a.sel')?.dataset.p), id, 'selected nav entry for ' + id); if (id === 'chat') assert.equal(await p.locator('#main iframe').count(), 1, 'chat page is the chat panel'); else assert.ok((await text(p)).length > 20, id + ' page is empty'); assert.equal(await p.locator('#main .banner.err').count(), 0, id + ' shows a page error'); }
  const titles = { home: 'Overview', accounts: 'Platforms & Accounts', multistream: 'Multistream', vertical: 'Vertical stream', automation: 'Automation Studio', obs: 'OBS Control', settings: 'Settings', chat: 'Chat' };
  for (const [id, t] of Object.entries(titles)) { await p.evaluate((h) => { location.hash = '#' + h; }, id); await p.waitForTimeout(250); assert.equal(await p.textContent('#pageTitle'), t); }
  await p.close(); assert.deepEqual(errors, []);
});

test('Multistream Studio: real OBS outputs, shared-encode relay clearly labelled, independent outputs marked not implemented', async () => {
  const p = await open(c, 'multistream-legacy'); await p.waitForSelector('text=Stream status'); await p.waitForSelector('text=aitum_out_1'); const t = await text(p); await shot(p, '02-multistream');
  assert.match(t, /adv_stream/); assert.match(t, /aitum_out_1/); assert.match(t, /1 of 2 OBS outputs active/); assert.match(t, /OBS is not streaming/);
  assert.match(t, /Shared-encode destinations/); assert.match(t, /exactly the stream OBS encodes/); assert.match(t, /cannot give one of them its own bitrate or encoder/);
  assert.match(t, /Independent outputs/); assert.match(t, /Not implemented/); assert.match(t, /Aitum Multistream is detected in OBS \(version 1\.0\.8\)/); assert.match(t, /IXC does not edit them yet/);
  assert.doesNotMatch(t, /per-destination bitrate (is )?(supported|available)/i);
  assert.equal(await p.locator('#msStart').count(), 1, 'the relay controls that work are still there'); assert.equal(await p.locator('#msAdd').count(), 1);
  // refresh asks OBS again
  const before = obs.st.calls.filter(x => x === 'GetStats').length; await p.click('[data-act=capsRefresh]'); await until(() => obs.st.calls.filter(x => x === 'GetStats').length > before, 5000, 'refresh asked OBS'); await p.close(); assert.deepEqual(errors, []);
});

test('Vertical Studio: shows what OBS reports, no vertical component detected, nothing to click except Refresh, unbuilt parts labelled', async () => {
  const p = await open(c, 'vertical-legacy'); await p.waitForSelector('text=Multiple canvases'); const t = await text(p); await shot(p, '03-vertical');
  assert.match(t, /No vertical canvas component detected in OBS/); assert.match(t, /Multiple canvases/); assert.match(t, /1920×1080 base/); assert.match(t, /None - there is no vertical output to show/);
  assert.ok((t.match(/Not implemented/g) || []).length >= 3, 'unbuilt parts are labelled'); assert.match(t, /resized preview is not vertical support/);
  const buttons = await p.$$eval('#main button', a => a.map(x => x.id || x.textContent.trim())); assert.deepEqual(buttons, ['vRefresh'], 'only Refresh is a control: ' + buttons.join(','));
  const before = obs.st.calls.filter(x => x === 'GetStats').length; await p.click('#vRefresh'); await until(() => obs.st.calls.filter(x => x === 'GetStats').length > before, 5000, 'refresh asked OBS');
  await p.close(); assert.deepEqual(errors, []);
});

test('Automation Studio: real settings, working links to the existing pages, unbuilt features labelled', async () => {
  await settings(c, { 'events.follow.on': true, 'events.raid.on': true });
  const p = await open(c, 'automation'); await p.waitForSelector('text=What runs automatically'); let t = await text(p); await shot(p, '04-automation');
  assert.match(t, /Chat commands/); assert.match(t, /Timers/); assert.match(t, /on for: New follower, Raid \/ host/); assert.match(t, /no Streamer\.bot or other program is needed/);
  assert.ok((t.match(/Not implemented/g) || []).length >= 5, 'five unbuilt areas labelled'); assert.doesNotMatch(t, /simulated|demo/i);
  await p.click('[data-go=timers]'); await p.waitForFunction(() => location.hash === '#timers' && document.querySelector('#pageTitle').textContent === 'Timers');
  await p.evaluate(() => { location.hash = '#automation'; }); await p.waitForSelector('text=What runs automatically'); await p.click('[data-go=obs]'); await p.waitForFunction(() => location.hash === '#obs');
  // a changed setting shows up
  await settings(c, { 'events.follow.on': false, 'events.raid.on': false }); await p.evaluate(() => { location.hash = '#automation'; }); await p.waitForSelector('text=What runs automatically');
  await until(async () => /all switched off/.test(await text(p)), 10000, 'the page follows the changed setting by itself');
  await p.close(); assert.deepEqual(errors, []);
});

test('OBS Control: the existing controls plus the read-only report from OBS', async () => {
  const p = await open(c, 'obs'); await p.waitForSelector('text=What this OBS can do'); await p.waitForSelector('text=obs-websocket'); const t = await text(p); await shot(p, '05-obs');
  assert.match(t, /Connection/); assert.match(t, /OBS 31\.0\.0 · obs-websocket 5\.5\.0/); assert.match(t, /Main canvas 1920×1080/); assert.match(t, /aitum_out_1/); assert.match(t, /Aitum Multistream detected/); assert.match(t, /Aitum Vertical Canvas not detected/);
  assert.match(t, /nothing was changed and no stream key or password was requested/); assert.equal(await p.locator('#oTest').count(), 1, 'existing connection controls kept');
  await p.close(); assert.deepEqual(errors, []);
});

test('without OBS the new pages say so plainly instead of showing made-up state', async () => {
  for (const id of ['multistream-legacy', 'vertical-legacy', 'obs']) { const p = await open(c2, id); await p.waitForSelector('text=OBS information is not available', { timeout: 15000 }).catch(async () => { await p.waitForSelector('text=OBS information is not available', { timeout: 15000 }); });
    const t = await text(p); assert.match(t, /OBS information is not available/); assert.doesNotMatch(t, /OBS is streaming|Aitum .* detected \(/); if (id === 'vertical-legacy') { await shot(p, '06-vertical-no-obs'); assert.deepEqual(await p.$$eval('#main button', a => a.map(x => x.id)), ['vRefresh']); } await p.close(); }
  assert.deepEqual(errors, []);
});
