// UI tests in a real Chromium: every dashboard page, the first-run wizard, docks, overlays and the phone page load without
// script errors and react to live data. Screenshots go to tests/ui/shots/ (for a human look).
import { test, before, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { startCore, api, inject, settings, page as wsPage, startRelay, sleep, until, fakeObs } from '../integration/harness.mjs';

const shots = path.join(path.dirname(fileURLToPath(import.meta.url)), 'shots'); fs.mkdirSync(shots, { recursive: true });
let c, b, obs, relay; const errors = [];
const exe = fs.existsSync('/opt/pw-browsers/chromium') ? '/opt/pw-browsers/chromium' : undefined;
before(async () => { obs = await fakeObs(); relay = await startRelay(); c = await startCore({ config: { general: { firstRunDone: false }, obs: { websocketUrl: obs.url } }, env: { IXC_EP_RELAY: relay.url } });
  b = await chromium.launch({ executablePath: exe, args: ['--no-sandbox', '--autoplay-policy=no-user-gesture-required'] }); });
after(async () => { await b?.close(); await c?.stop(); await obs?.close(); await relay?.stop(); });
async function open(url, w = 1280, h = 860) { const p = await b.newPage({ viewport: { width: w, height: h } });
  p.on('pageerror', e => errors.push(url + ': ' + e.message)); p.on('console', m => { if (m.type() === 'error' && !/youtube|ERR_|net::|Failed to load resource|googleapis|ytimg|jtvnw|kick\.com|fonts/i.test(m.text())) errors.push(url + ': ' + m.text()); });
  await p.goto(c.url.replace('127.0.0.1', 'localhost') + url); return p; }
const shot = (p, n) => p.screenshot({ path: path.join(shots, n + '.png') });

test('first-run wizard walks through every step', async () => {
  const p = await open('/app/'); await p.waitForSelector('#wzCard h2'); await shot(p, '01-wizard-welcome');
  for (const step of ['obs', 'platforms', 'sound', 'phone', 'overlays', 'test']) { await p.click('#wzNext'); await p.waitForTimeout(700); await shot(p, '02-wizard-' + step); }
  await until(async () => await p.locator('#wzCheck .stepline, #wzCheck .banner').count() > 0, 15000, 'system test shown');
  await p.click('#wzNext'); await p.waitForSelector('text=Setup complete'); await shot(p, '03-wizard-done'); await p.click('#wzNext');
  await until(async () => (await api(c, '/api/settings')).body.values['general.firstRunDone'] === true, 5000, 'first run done saved');
  await p.close();
});

test('every dashboard page renders with live data and no script errors', async () => {
  await settings(c, { 'general.firstRunDone': true, 'platforms.kick.enabled': true, 'platforms.kick.channel': 'demo' });
  for (let i = 0; i < 6; i++) await inject(c, { platform: ['twitch', 'kick', 'youtube'][i % 3], id: 'ui' + i, name: ['Rushkaa', 'ShadowSniper', 'Aman Verma'][i % 3], text: ['GG how many kills?', 'that spray transfer was insane', 'first time here, love the setup'][i % 3] + ' #' + i, mod: i === 1 });
  const p = await open('/app/');
  for (const pg of ['home', 'chat', 'music', 'requests', 'accounts', 'overlays', 'phone', 'tts', 'voices', 'commands', 'filters', 'check', 'settings']) {
    await p.evaluate((h) => { location.hash = h; }, '#' + pg); await p.waitForTimeout(pg === 'check' ? 1200 : 700);
    assert.ok(await p.locator('#main').evaluate(el => el.innerText.length > 20 || el.querySelector('iframe')), pg + ' has content'); await shot(p, '10-page-' + pg); }
  // controls save settings
  await p.evaluate(() => { location.hash = '#tts'; }); await p.waitForTimeout(600);
  await p.locator('label.switch:has([data-k="tts.on"])').click(); await until(async () => (await api(c, '/api/settings')).body.values['tts.on'] === true, 4000, 'toggle saved');
  await p.evaluate(() => { location.hash = '#check'; }); await p.waitForTimeout(500); await p.click('#run'); await p.waitForSelector('#res .check', { timeout: 15000 }); await shot(p, '11-system-check');
  // phone page shows a QR code (through the relay)
  await p.evaluate(() => { location.hash = '#phone'; }); await until(async () => !(await p.locator('#phPair').isDisabled()), 30000, 'relay connected');
  await p.click('#phPair'); await p.waitForSelector('#qrBox svg', { timeout: 10000 }); await shot(p, '12-phone-qr');
  // overlay editor: change a style -> saved and applied live
  await p.evaluate(() => { location.hash = '#overlays?o=nowplaying'; }); await p.waitForTimeout(800);
  await p.selectOption('select[data-o="layout"]', 'compact'); await until(async () => (await api(c, '/api/settings')).body.values['overlays.nowplaying']?.layout === 'compact', 5000, 'overlay style saved');
  await p.waitForTimeout(600); await shot(p, '13-overlay-editor');
  await p.click('#ovAdd'); await until(() => obs.st.inputs['IXC Now Playing'], 5000, 'overlay added to OBS');
  // dock mode (inside OBS) is compact
  const d = await open('/app/?dock=1#home', 380, 800); await d.waitForTimeout(800); await shot(d, '14-dashboard-as-dock'); await d.close();
  // narrow window
  const n = await open('/app/#home', 600, 900); await n.waitForTimeout(800); await shot(n, '15-dashboard-narrow'); await n.close();
  await p.close();
});

test('docks and the phone page', async () => {
  const chat = await open('/chat/chat.html?dock=1', 420, 800); await chat.waitForSelector('.msg'); assert.ok(await chat.locator('.msg').count() >= 6);
  await inject(c, { platform: 'twitch', id: 'live1', name: 'NewViewer', text: 'just arrived Kappa', emotes: [] }); await chat.waitForSelector('text=just arrived');
  await chat.locator('.msg').first().click(); await chat.waitForSelector('#menu.show'); await shot(chat, '20-chat-dock-menu');
  await chat.locator('#menu button[data-a="reply"]').click(); assert.match(await chat.inputValue('#msgIn'), /^@/);
  await chat.close();
  const music = await open('/music/dock.html', 420, 800); await music.waitForTimeout(800); await shot(music, '21-music-dock'); await music.close();
  // phone page from the relay (pairing is tested in the integration tests; here: it renders and explains what to do)
  const pr = (await api(c, '/api/remote/pair', {})).body; const ph = await b.newPage({ viewport: { width: 390, height: 844 }, isMobile: true });
  ph.on('pageerror', e => errors.push('phone: ' + e.message)); await ph.goto(pr.url); await ph.waitForSelector('#conn.ok', { timeout: 15000 });
  await ph.waitForTimeout(800); await shot(ph, '22-phone-music');
  for (const t of ['chat', 'voice', 'status']) { await ph.click(`#tabs button[data-t="${t}"]`); await ph.waitForTimeout(500); await shot(ph, '23-phone-' + t); }
  assert.equal((await api(c, '/api/remote/status')).body.devices.length, 1, 'phone paired from the real page');
  await ph.close();
});

test('overlays render transparent, with demo data in preview mode', async () => {
  for (const o of ['nowplaying', 'queue', 'chat', 'viewers', 'alerts', 'tts', 'status']) {
    const p = await open(`/overlay/${o}.html?preview=1`, 800, 400); await p.waitForTimeout(o === 'chat' ? 2500 : 1200);
    const bg = await p.evaluate(() => getComputedStyle(document.body).backgroundColor); assert.match(bg, /rgba\(0, 0, 0, 0\)|transparent/, o + ' background is transparent');
    await shot(p, '30-overlay-' + o); await p.close(); }
  assert.deepEqual(errors, [], 'no script errors:\n' + errors.join('\n'));
});
