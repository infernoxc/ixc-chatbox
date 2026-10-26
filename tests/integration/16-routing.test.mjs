// Per-platform audio: music and the chat voice (TTS) switched ON/OFF per platform, independently, through OBS audio tracks.
// Checked against the fake OBS's real track state (what OBS would send to each output), not against IXC's own idea of it.
import { test, after } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, fakeObs, until, page } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });

test('Routing: music and TTS ON/OFF per platform, applied in OBS and read back', async () => {
  const obs = await fakeObs({}); cleanup.push(obs.close);
  const c = await startCore({ config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  await until(() => obs.st.tracks['IXC Music Player'] && obs.st.tracks['IXC TTS'], 10000, 'sources created');
  await until(async () => (await api(c, '/api/music/route')).body.ok && (await api(c, '/api/tts/route')).body.ok, 8000, 'both routed');
  const T = (n) => { const t = obs.st.tracks[n]; return [1, 2, 3].map(k => t[k] ? 1 : 0).join(''); };
  assert.equal(T('IXC Music Player'), '111'); assert.equal(T('IXC TTS'), '111');

  // music off for Kick only: Twitch + YouTube keep it, mode becomes custom
  let r = await api(c, '/api/music/route', { platform: 'kick', on: false }); assert.equal(r.body.ok, true, JSON.stringify(r.body));
  assert.equal(r.body.mode, 'custom'); assert.deepEqual(r.body.on, { twitch: true, kick: false, youtube: true });
  assert.equal(T('IXC Music Player'), '101'); assert.equal(T('IXC TTS'), '111', 'TTS untouched');
  // TTS only on Twitch
  await api(c, '/api/tts/route', { platform: 'kick', on: false }); r = await api(c, '/api/tts/route', { platform: 'youtube', on: false });
  assert.equal(r.body.mode, 'twitch'); assert.equal(T('IXC TTS'), '100'); assert.equal(T('IXC Music Player'), '101');
  // everything off and back on
  for (const p of ['twitch', 'youtube']) r = await api(c, '/api/music/route', { platform: p, on: false });
  assert.equal(r.body.mode, 'off'); assert.equal(T('IXC Music Player'), '000');
  for (const p of ['twitch', 'kick', 'youtube']) r = await api(c, '/api/music/route', { platform: p, on: true });
  assert.equal(r.body.mode, 'all'); assert.equal(T('IXC Music Player'), '111');
  // tracks 4-6 (recording / VOD) never touched
  assert.equal(obs.st.tracks['IXC Music Player'][4], true); assert.equal(obs.st.tracks['IXC TTS'][6], true);
  // bad input refused, nothing changed
  r = await api(c, '/api/music/route', { platform: 'tiktok', on: false }); assert.equal(r.status, 400);
  r = await api(c, '/api/tts/route', { mode: 'custom' }); assert.equal(r.status, 400);
  assert.equal(T('IXC Music Player'), '111');

  // the dock / phone path (WebSocket) does the same
  const ws = await page(c, 'dock', ['music.state', 'tts']);
  ws.send({ type: 'tts.route', reqId: 'x1', platform: 'kick', on: true });
  const m = await ws.wait(x => x.type === 'tts.routed' && x.reqId === 'x1', 8000);
  assert.equal(m.result.ok, true); assert.equal(T('IXC TTS'), '110');
  await ws.close();

  // changed by hand in OBS: reported, not hidden
  obs.st.tracks['IXC TTS'][1] = false; obs.event('InputAudioTracksChanged', { inputName: 'IXC TTS', inputAudioTracks: obs.st.tracks['IXC TTS'] });
  await until(async () => /by hand/.test((await api(c, '/api/tts/route')).body.status), 5000, 'hand change reported');

  // settings survive a restart and are re-applied to OBS when IXC connects again (OBS was changed meanwhile)
  await api(c, '/api/music/route', { platform: 'kick', on: false });
  await c.stop(); for (const k of [1, 2, 3]) obs.st.tracks['IXC Music Player'][k] = true;   // changed while IXC is closed (before, this raced IXC's apply on connect)
  let c2 = await c.restart(); cleanup.push(() => c2.stop());
  await until(async () => { const r2 = (await api(c2, '/api/music/route')).body; return r2.ok && r2.mode === 'custom' && T('IXC Music Player') === '101' ? r2 : null; }, 15000, 'music route kept and re-applied after restart');
  const t2 = (await api(c2, '/api/tts/route')).body; assert.deepEqual(t2.on, { twitch: true, kick: true, youtube: false }, 'TTS choice kept separately');
});


// renamed in OBS (the user's collection had "MUSIC — IXC Music Player"): routing follows the source instead of reporting it missing
test('Routing: follows a source renamed in OBS, live and while IXC was closed', async () => {
  const obs = await fakeObs({}); cleanup.push(obs.close);
  const c = await startCore({ config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  await until(async () => (await api(c, '/api/music/route')).body.ok, 10000, 'music routed');
  const ren = (a, b) => { obs.st.inputs[b] = obs.st.inputs[a]; delete obs.st.inputs[a]; obs.st.tracks[b] = obs.st.tracks[a]; delete obs.st.tracks[a];
    for (const k of Object.keys(obs.st.items)) obs.st.items[k] = obs.st.items[k].map(n => n === a ? b : n); };
  // live rename: the InputNameChanged event updates the saved name
  ren('IXC Music Player', 'MUSIC — IXC Music Player'); obs.event('InputNameChanged', { oldInputName: 'IXC Music Player', inputName: 'MUSIC — IXC Music Player' });
  await until(async () => { const r = (await api(c, '/api/music/route')).body; return r.input === 'MUSIC — IXC Music Player' && r.ok ? r : null; }, 8000, 'live rename followed');
  // renamed while IXC was closed: found again by the player page address on the next apply
  await c.stop(); ren('MUSIC — IXC Music Player', 'Song player'); ren('IXC TTS', 'CHAT — IXC TTS');
  const c2 = await c.restart(); cleanup.push(() => c2.stop());
  await until(async () => { const m = (await api(c2, '/api/music/route')).body, t = (await api(c2, '/api/tts/route')).body;
    return m.ok && m.input === 'Song player' && t.ok && t.input === 'CHAT — IXC TTS' ? m : null; }, 15000, 'offline rename found by address');
  assert.equal(Object.keys(obs.st.inputs).filter(n => /player/i.test(n)).length, 1, 'no duplicate player source created');
});
