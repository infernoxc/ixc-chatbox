// Three-platform stream status: read from OBS's / Aitum's / obs-multi-rtmp's saved settings (keys never returned) plus OBS's live outputs.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { startCore, api, fakeObs } from './harness.mjs';
const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const KEY = 'live_FAKEKEY_must_never_appear_123';
function obsDir() {
  const d = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-obscfg-')); const prof = path.join(d, 'basic', 'profiles', 'Untitled'); fs.mkdirSync(prof, { recursive: true });
  fs.mkdirSync(path.join(d, 'plugin_config', 'aitum-multistream'), { recursive: true });
  fs.writeFileSync(path.join(d, 'user.ini'), '[Basic]\nProfile=Untitled\nProfileDir=Untitled\n');
  fs.writeFileSync(path.join(prof, 'service.json'), JSON.stringify({ type: 'rtmp_custom', settings: { server: 'rtmp://127.0.0.1:19350/live', key: KEY } }));
  fs.writeFileSync(path.join(d, 'plugin_config', 'aitum-multistream', 'config.json'), JSON.stringify({ profiles: [{ name: 'Untitled', outputs: [
    { name: 'Twitch Output', stream_server: 'rtmp://aps30.contribute.live-video.net/app', stream_key: KEY, audio_track: 0 },
    { name: 'YouTube Output', stream_server: 'rtmps://a.rtmps.youtube.com:443/live2', stream_key: KEY, audio_track: 2 }] }] }));
  fs.writeFileSync(path.join(prof, 'obs-multi-rtmp.json'), JSON.stringify({ audio_configs: [{ id: 1, mixerId: 1 }, { id: 2, mixerId: 2 }], targets: [
    { name: 'KICK (track 2)', 'audio-config': 1, 'service-param': { server: '', key: KEY } },
    { name: 'YOUTUBE (track 3)', 'audio-config': 2, 'service-param': { server: 'rtmp://a.rtmp.youtube.com/live2', key: KEY } }] }));
  return d; }

test('Outputs: per-platform state, duplicates and missing server flagged, dead IXC relay flagged, keys never returned', async () => {
  const obs = await fakeObs({ outputs: [{ outputName: 'adv_stream', outputKind: 'rtmp_output', outputActive: false }, { outputName: 'aitum_multi_output_twitch', outputKind: 'rtmp_output', outputActive: true }] }); cleanup.push(obs.close);
  const c = await startCore({ env: { IXC_EP_OBS_CONFIG_DIR: obsDir() }, config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  let r; for (let i = 0; i < 40; i++) { r = (await api(c, '/api/outputs')).body; if (r.obs === 'connected') break; await new Promise(s => setTimeout(s, 250)); }
  assert.ok(!JSON.stringify(r).includes(KEY), 'stream key never returned');
  assert.equal(r.platforms.twitch.state, 'STREAMING'); assert.match(r.platforms.twitch.liveNote, /not checked/);
  assert.equal(r.platforms.youtube.state, 'ERROR'); assert.match(r.platforms.youtube.issues.join(' '), /set up 2 times/);
  assert.equal(r.platforms.kick.state, 'ERROR'); assert.match(r.platforms.kick.issues.join(' '), /no server address/);
  assert.equal(r.platforms.kick.outputs[0].audioTrack, 2); assert.equal(r.platforms.youtube.outputs[1].audioTrack, 3);
  assert.match(r.mainIssue, /old multistream relay/);
});

// evidence run against this PC's real OBS settings folder (read-only; OBS itself is a fake, so nothing real is contacted or changed)
test('Outputs: this PC\'s real OBS settings (states only)', { skip: !process.env.IXC_REAL_OBS_DIR ? 'set IXC_REAL_OBS_DIR' : false }, async () => {
  const obs = await fakeObs({}); cleanup.push(obs.close);
  const c = await startCore({ env: { IXC_EP_OBS_CONFIG_DIR: process.env.IXC_REAL_OBS_DIR }, config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  const r = (await api(c, '/api/outputs')).body;
  for (const p of ['twitch', 'youtube', 'kick']) console.log('REAL', p, r.platforms[p].state, JSON.stringify(r.platforms[p].issues), r.platforms[p].outputs.map(o => o.via + ':' + o.name + ':track' + o.audioTrack + ':key' + (o.keySet ? 'set' : 'missing')).join(' | '));
  console.log('REAL main', r.mainIssue || 'ok');
  assert.ok(!/live_[A-Za-z0-9]{8,}|sk_|[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}/.test(JSON.stringify(r)), 'nothing key-shaped in the result');
});
// the user's real shape: complete Aitum outputs for all three + leftover multi-rtmp targets without key/server -> READY, leftovers only noted
test('Outputs: incomplete leftover targets are not duplicates and do not make a platform ERROR', async () => {
  const d = obsDir(); const a = path.join(d, 'plugin_config', 'aitum-multistream', 'config.json'); const j = JSON.parse(fs.readFileSync(a, 'utf8'));
  j.profiles[0].outputs.push({ name: 'Kick Output', stream_server: 'rtmps://fa723fc1b171.global-contribute.live-video.net/app', stream_key: KEY, audio_track: 1 }); fs.writeFileSync(a, JSON.stringify(j));
  const mr = path.join(d, 'basic', 'profiles', 'Untitled', 'obs-multi-rtmp.json'); const m = JSON.parse(fs.readFileSync(mr, 'utf8'));
  m.targets[0]['service-param'] = { server: '', key: '' }; m.targets[1]['service-param'].key = ''; fs.writeFileSync(mr, JSON.stringify(m));
  const obs = await fakeObs({}); cleanup.push(obs.close);
  const c = await startCore({ env: { IXC_EP_OBS_CONFIG_DIR: d }, config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  let r; for (let i = 0; i < 40; i++) { r = (await api(c, '/api/outputs')).body; if (r.obs === 'connected') break; await new Promise(s => setTimeout(s, 250)); }
  for (const p of ['twitch', 'youtube', 'kick']) assert.equal(r.platforms[p].state, 'READY', p + ': ' + JSON.stringify(r.platforms[p].issues));
  assert.match(r.platforms.kick.notes.join(' '), /no server address or stream key \(unused/); assert.match(r.platforms.youtube.notes.join(' '), /no stream key \(unused/);
  assert.equal(r.platforms.kick.outputs.find(o => o.via === 'Aitum Multistream').audioTrack, 2);
});

// Start / Stop through Aitum: off until the safe local test set streams.aitumParam; READY only, no duplicate starts, stop needs confirmation when live
test('Outputs: Aitum start/stop controls (mock Aitum)', async () => {
  const d = obsDir(); const a = path.join(d, 'plugin_config', 'aitum-multistream', 'config.json'); const j = JSON.parse(fs.readFileSync(a, 'utf8'));
  j.profiles[0].outputs.push({ name: 'Kick Output', stream_server: 'rtmps://fa723fc1b171.global-contribute.live-video.net/app', stream_key: '', audio_track: 1 }); fs.writeFileSync(a, JSON.stringify(j));   // Kick: no key -> never started
  fs.writeFileSync(path.join(d, 'basic', 'profiles', 'Untitled', 'obs-multi-rtmp.json'), JSON.stringify({ targets: [] }));
  const aitum = { param: 'output', names: ['Twitch Output', 'YouTube Output', 'Kick Output'], calls: [] }; const outputs = [];
  const obs = await fakeObs({ aitum, outputs }); cleanup.push(obs.close);
  const c = await startCore({ env: { IXC_EP_OBS_CONFIG_DIR: d }, config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  let r; for (let i = 0; i < 40; i++) { r = (await api(c, '/api/outputs')).body; if (r.obs === 'connected') break; await new Promise(s => setTimeout(s, 250)); }
  assert.equal(r.controls.enabled, false); let x = await api(c, '/api/outputs/start', { platform: 'all' }); assert.equal(x.status, 400); assert.match(x.body.error, /AITUM-SINK-TEST/); assert.equal(aitum.calls.length, 0, 'nothing sent while off');
  assert.equal((await api(c, '/api/settings', { 'streams.aitumParam': 'output' })).status, 200);
  x = (await api(c, '/api/outputs/start', { platform: 'all' })).body;
  assert.match(x.results.twitch.result, /start sent/); assert.match(x.results.youtube.result, /start sent/); assert.match(x.results.kick.result, /skipped/);
  assert.deepEqual(aitum.calls.filter(k => k.type === 'start_output').map(k => k.data.output), ['Twitch Output', 'YouTube Output'], 'only READY outputs, by name in the proven field');
  r = (await api(c, '/api/outputs')).body; assert.equal(r.platforms.twitch.state, 'STREAMING'); assert.equal(r.platforms.youtube.state, 'STREAMING');
  x = (await api(c, '/api/outputs/start', { platform: 'twitch' })).body; assert.match(x.results.twitch.result, /already running/); assert.equal(aitum.calls.filter(k => k.type === 'start_output').length, 2, 'no duplicate start');
  x = (await api(c, '/api/outputs/stop', { platform: 'all' })).body; assert.equal(x.needConfirm, true); assert.equal(aitum.calls.filter(k => k.type === 'stop_output').length, 0);
  x = (await api(c, '/api/outputs/stop', { platform: 'all', confirm: true })).body; assert.match(x.results.twitch.result, /stop sent/); assert.match(x.results.kick.result, /skipped/);
  r = (await api(c, '/api/outputs')).body; assert.equal(r.platforms.twitch.state, 'READY'); assert.equal(r.platforms.youtube.state, 'READY'); assert.equal(r.platforms.kick.state, 'ERROR');
});