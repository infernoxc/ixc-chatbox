// OBS: connection with the password read from OBS's own settings, automatic setup (scene + sources in every scene),
// audio routing per platform, turning on the WebSocket server, adding docks, adding overlays, OBS restarts.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { startCore, api, settings, fakeObs, sleep, until, freePort } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
function obsConfig(dir, ws, ini) {
  const d = path.join(dir, 'obs-config'); fs.mkdirSync(path.join(d, 'plugin_config/obs-websocket'), { recursive: true });
  if (ws) fs.writeFileSync(path.join(d, 'plugin_config/obs-websocket/config.json'), JSON.stringify(ws));
  if (ini) fs.writeFileSync(path.join(d, 'user.ini'), ini); return d; }

test('OBS: connects with the password from OBS\'s settings, sets itself up and routes music per platform', async () => {
  const obs = await fakeObs({ password: 'pw-from-obs', notReadyOnce: true }); cleanup.push(obs.close);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-obs-')); obsConfig(dir, { server_enabled: true, server_port: obs.port, server_password: 'pw-from-obs', auth_required: true });
  const c = await startCore({ dir, config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  await until(() => obs.st.inputs['IXC Music Player'] && obs.st.inputs['IXC TTS'], 10000, 'sources created');
  assert.ok(obs.st.scenes.includes('IXC Audio')); assert.ok(obs.st.items['Main'].includes('IXC Audio') && obs.st.items['BRB'].includes('IXC Audio'), 'nested into every scene');
  const pl = obs.st.inputs['IXC Music Player'].settings; assert.match(pl.url, new RegExp('http://localhost:' + c.port + '/music/player.html')); assert.equal(pl.reroute_audio, true); assert.equal(pl.shutdown, false);
  // first routing call got "not ready" from OBS, IXC retried by itself
  await until(async () => (await api(c, '/api/music/route')).body.ok === true, 8000, 'route applied');
  let r = await api(c, '/api/music/route', { mode: 'kick' }); assert.equal(r.body.ok, true, JSON.stringify(r.body));
  assert.deepEqual(obs.st.tracks['IXC Music Player'], { 1: false, 2: true, 3: false, 4: true, 5: true, 6: true }, 'only Kick\'s track, other tracks untouched');
  await settings(c, { 'music.routing.tracks.youtube': ['3', '4'] }); r = await api(c, '/api/music/route', { mode: 'youtube' });
  assert.deepEqual(obs.st.tracks['IXC Music Player'], { 1: false, 2: false, 3: true, 4: true, 5: true, 6: true });
  r = await api(c, '/api/music/route', { mode: 'loud' }); assert.equal(r.status, 400);
  // overlays go into the scene that is on air
  const ov = await api(c, '/api/obs/overlay', { name: 'Now Playing', path: '/overlay/nowplaying.html', width: 600, height: 140 }); assert.equal(ov.body.ok, true);
  assert.ok(obs.st.items['Main'].includes('IXC Now Playing')); assert.equal(obs.st.inputs['IXC Now Playing'].settings.reroute_audio, false);
  // System check sees it all
  const h = (await api(c, '/api/health')).body; const it = (id) => h.items.find(x => x.id === id);
  assert.equal(it('obsws').level, 'ok'); assert.equal(it('obssetup').level, 'ok'); assert.equal(it('audio').level, 'ok');
  // OBS closes and opens again: IXC reconnects and re-applies the route
  const port = obs.port; await obs.close(); await until(async () => (await api(c, '/api/status')).body.obs.connected === false, 8000, 'noticed OBS closed');
  const obs2 = await fakeObs({ password: 'pw-from-obs', scenes: ['Main'] }); cleanup.push(obs2.close);
  // the new fake listens elsewhere: point IXC at it like a changed OBS port
  await settings(c, { 'obs.websocketUrl': obs2.url });
  await until(async () => (await api(c, '/api/status')).body.obs.connected === true, 15000, 'reconnected');
  await until(() => obs2.st.calls.includes('SetInputAudioTracks') || obs2.st.calls.includes('GetInputSettings'), 10000, 'route re-applied');
});

test('OBS: a wrong password is reported, never retried in a tight loop', async () => {
  const obs = await fakeObs({ password: 'right' }); cleanup.push(obs.close);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-obs-')); obsConfig(dir, { server_enabled: true, server_port: obs.port, server_password: 'wrong' });
  const c = await startCore({ dir, config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  await until(async () => { const s = (await api(c, '/api/status')).body.obs; return s.state === 'auth' || /password/i.test(s.detail) ? s : null; }, 10000, 'auth problem shown');
  const n = obs.conns.length; await sleep(4000); assert.ok(obs.conns.length - n <= 1, 'no reconnect storm (' + (obs.conns.length - n) + ' tries in 4 s)');
});

test('OBS: turning on the WebSocket server and adding the panels (OBS closed)', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-obs-')); const cfgDir = obsConfig(dir, null, '[General]\nX=1\n\n[BasicWindow]\nDockState=abc\n');
  const c = await startCore({ dir, config: { obs: { websocketUrl: 'auto' } } }); cleanup.push(() => c.stop());
  let r = await api(c, '/api/repair', { action: 'obs.websocket' }); assert.equal(r.body.ok, true, JSON.stringify(r.body));
  const ws = JSON.parse(fs.readFileSync(path.join(cfgDir, 'plugin_config/obs-websocket/config.json'), 'utf8'));
  assert.equal(ws.server_enabled, true); assert.equal(ws.server_port, 4455); assert.equal(ws.auth_required, true); assert.ok(ws.server_password.length >= 12, 'a strong password was generated');
  r = await api(c, '/api/repair', { action: 'obs.websocket' }); assert.equal(r.body.ok, true, 'running it again is harmless');
  assert.equal(JSON.parse(fs.readFileSync(path.join(cfgDir, 'plugin_config/obs-websocket/config.json'), 'utf8')).server_password, ws.server_password, 'password kept');
  r = await api(c, '/api/repair', { action: 'obs.docks' }); assert.equal(r.body.ok, true, JSON.stringify(r.body));
  r = await api(c, '/api/repair', { action: 'obs.docks' }); assert.equal(r.body.ok, true);
  const ini = fs.readFileSync(path.join(cfgDir, 'user.ini'), 'utf8'); const docks = JSON.parse(ini.match(/^ExtraBrowserDocks=(.*)$/m)[1]);
  assert.deepEqual(docks.map(d => d.title).sort(), ['IXC', 'IXC ChatBox', 'IXC Music'], 'each panel once'); assert.match(ini, /DockState=abc/, 'the rest of OBS\'s settings untouched');
  assert.ok(fs.readdirSync(cfgDir).some(f => f.startsWith('user.ini.before-ixc-')), 'backup made');
  const d = (await api(c, '/api/obs/detect')).body; assert.equal(d.websocketEnabled, true); assert.equal(d.usedBefore, true);
});
