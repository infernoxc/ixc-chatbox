// OBS control over obs-websocket 5 without Streamer.bot: connection form with Test, streaming / recording state from OBS's own
// events, scene switching, show / hide sources, OBS actions in chat commands. Fake OBS (same protocol as OBS 28+).
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path';
import { startCore, api, inject, fakeObs, sleep, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });

test('OBS control: connect with address + password, streaming state, scenes, sources, !brb, OBS gone', async () => {
  const obs = await fakeObs({ password: 'right-password', scenes: ['Main', 'BRB'] }); cleanup.push(obs.close);
  obs.st.items.Main.push('Webcam', 'Alerts');
  const c = await startCore({ config: { platforms: { streamerbot: { mode: 'off' } }, obs: { websocketUrl: 'auto' } } }); cleanup.push(() => c.stop());
  let st = (await api(c, '/api/obs/state')).body; assert.equal(st.connected, false);
  // scenes need OBS
  let r = await api(c, '/api/obs/scenes'); assert.equal(r.status, 409); assert.match(r.body.error, /isn't connected/);
  // a bad address is explained
  r = (await api(c, '/api/obs/connection', { address: 'not an address', password: 'x' })).body; assert.equal(r.ok, false); assert.match(r.error, /127\.0\.0\.1:4455/);
  // wrong password: Test connection says so
  r = (await api(c, '/api/obs/connection', { address: '127.0.0.1:' + obs.port, password: 'wrong', waitSec: 8 })).body; assert.equal(r.ok, false); assert.match(r.error, /password/i);
  // right password: connected, version shown; the password is stored encrypted, not in config.json
  r = (await api(c, '/api/obs/connection', { address: '127.0.0.1:' + obs.port, password: 'right-password', waitSec: 10 })).body; assert.equal(r.ok, true, JSON.stringify(r));
  assert.equal(r.version, '31.0.0'); assert.equal(r.address, 'ws://127.0.0.1:' + obs.port + '/'); assert.equal(r.passwordSet, true);
  assert.ok(!fs.readFileSync(path.join(c.dir, 'config.json'), 'utf8').includes('right-password')); assert.ok(!fs.readFileSync(path.join(c.dir, 'secrets.dat')).toString('latin1').includes('right-password'));
  // streaming / recording come from OBS's own events
  await until(async () => (await api(c, '/api/obs/state')).body.streaming === false, 5000, 'not streaming');
  obs.event('StreamStateChanged', { outputActive: true, outputState: 'OBS_WEBSOCKET_OUTPUT_STARTED' });
  await until(async () => (await api(c, '/api/obs/state')).body.streaming === true, 5000, 'streaming');
  assert.equal((await api(c, '/api/status')).body.obs.streaming, true, 'the dashboard status says so too');
  obs.event('RecordStateChanged', { outputActive: true }); await until(async () => (await api(c, '/api/obs/state')).body.recording === true, 5000, 'recording');
  obs.event('StreamStateChanged', { outputActive: false }); await until(async () => (await api(c, '/api/obs/state')).body.streaming === false, 5000, 'stopped');
  // scenes
  r = (await api(c, '/api/obs/scenes')).body; for (const s of ['Main', 'BRB']) assert.ok(r.scenes.includes(s), s); assert.equal(r.current, 'Main');
  r = await api(c, '/api/obs/scene', { scene: 'BRB' }); assert.equal(r.status, 200); assert.equal(obs.st.current, 'BRB');
  await until(async () => (await api(c, '/api/obs/state')).body.scene === 'BRB', 5000, 'scene change seen');
  r = await api(c, '/api/obs/scene', { scene: 'Nope' }); assert.equal(r.status, 400); assert.match(r.body.error, /OBS: No source was found by the name of `Nope`/);
  // sources: hide, toggle back
  r = (await api(c, '/api/obs/sources?scene=Main')).body; assert.ok(r.sources.find(s => s.name === 'Webcam').visible);
  assert.equal((await api(c, '/api/obs/source', { scene: 'Main', source: 'Webcam', visible: 'hide' })).status, 200); assert.ok(obs.st.hidden.has('Main|Webcam'));
  assert.equal((await api(c, '/api/obs/source', { scene: 'Main', source: 'Webcam', visible: 'toggle' })).status, 200); assert.ok(!obs.st.hidden.has('Main|Webcam'));
  assert.equal((await api(c, '/api/obs/source', { scene: 'Main', source: 'Missing' })).status, 400);
  // chat commands with OBS actions: !brb (moderators) switches scene, !cam toggles the webcam
  const list = (await api(c, '/api/settings')).body.values['commands.list'];
  let s = await api(c, '/api/settings', { patch: { 'commands.list': list.concat([{ trigger: '!brb', response: '', permission: 'moderator', platforms: [], enabled: true, obs: { type: 'scene' } }]) } });
  assert.equal(s.status, 400); assert.match(s.body.error, /pick the scene/);
  s = await api(c, '/api/settings', { patch: { 'commands.list': list.concat([
    { trigger: '!brb', response: 'Be right back!', permission: 'moderator', platforms: [], enabled: true, obs: { type: 'scene', scene: 'BRB' } },
    { trigger: '!back', response: '', permission: 'moderator', platforms: [], enabled: true, obs: { type: 'scene', scene: 'Main' } },
    { trigger: '!cam', response: '', permission: 'moderator', platforms: [], enabled: true, obs: { type: 'toggle', scene: 'Main', source: 'Webcam' } }]) } });
  assert.equal(s.status, 200, JSON.stringify(s.body));
  await inject(c, { platform: 'twitch', id: 'b1', name: 'Mod', mod: true, text: '!back' }); await until(() => obs.st.current === 'Main', 4000, '!back');
  await inject(c, { platform: 'twitch', id: 'b2', name: 'Viewer', text: '!brb' }); await sleep(600); assert.equal(obs.st.current, 'Main', 'viewers can\'t switch scenes');
  await inject(c, { platform: 'kick', id: 'b3', name: 'Mod', mod: true, text: '!brb' }); await until(() => obs.st.current === 'BRB', 4000, '!brb by a moderator');
  await inject(c, { platform: 'kick', id: 'b4', name: 'Mod', mod: true, text: '!cam' }); await until(() => obs.st.hidden.has('Main|Webcam'), 4000, '!cam hides the webcam');
  // OBS closes: state becomes unknown (never a guessed "not streaming"), scene requests explain why
  await obs.close();
  await until(async () => (await api(c, '/api/obs/state')).body.connected === false, 10000, 'OBS gone');
  st = (await api(c, '/api/obs/state')).body; assert.equal(st.streaming, null); assert.equal(st.recording, null);
  assert.equal((await api(c, '/api/obs/scenes')).status, 409);
});
