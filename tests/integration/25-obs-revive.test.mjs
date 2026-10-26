// OBS opens before IXC answers (IXC starts with OBS): its IXC browser sources (music player, chat voice, overlays) fail to load and never retry.
// IXC must reload the page of every IXC source that has NOT connected a few seconds after it connects to OBS, and leave connected ones alone. Fake OBS.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import WebSocket from 'ws';
import { startCore, fakeObs, freePort, page, until, sleep } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
async function obsCall(obs, type, data) { const ws = new WebSocket(obs.url); const done = new Promise((res, rej) => { ws.on('error', rej); ws.on('message', (m) => { const j = JSON.parse(m); if (j.op === 0) ws.send(JSON.stringify({ op: 1, d: { rpcVersion: 1 } })); else if (j.op === 2) ws.send(JSON.stringify({ op: 6, d: { requestType: type, requestId: 'x', requestData: data } })); else if (j.op === 7) res(j.d); }); });   // handler first: OBS says hello at once
  const r = await done; ws.close(); return r; }

test('sources that did not connect are reloaded (refresh only); a source that connected is left alone', async () => {
  const obs = await fakeObs(); cleanup.push(obs.close); const port = await freePort();
  await obsCall(obs, 'CreateInput', { sceneName: 'Main', inputName: 'IXC Alerts test', inputKind: 'browser_source', inputSettings: { url: 'http://localhost:' + port + '/overlay/alerts.html' } });
  await obsCall(obs, 'CreateInput', { sceneName: 'Main', inputName: 'Someone elses overlay', inputKind: 'browser_source', inputSettings: { url: 'https://example.com/overlay/alerts?token=SECRET' } });   // not an IXC source: must never be touched
  const c = await startCore({ port, env: { IXC_EP_OBS_REVIVE_MS: '500' }, config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  // the music player connects by itself (like a source that loaded fine); the chat voice and the overlay do not
  const player = await page(c, 'player', ['music.player', 'reload']); cleanup.push(() => player.close());
  await until(() => (obs.st.pressed || []).length >= 2, 15000, 'unconnected sources reloaded');
  const pressed = obs.st.pressed; assert.ok(pressed.includes('IXC TTS|refreshnocache'), 'chat voice source reloaded: ' + pressed);
  assert.ok(pressed.includes('IXC Alerts test|refreshnocache'), 'overlay source reloaded: ' + pressed);
  assert.ok(!pressed.some(x => x.startsWith('IXC Music Player')), 'the connected music player is left alone');
  assert.ok(!pressed.some(x => /elses/.test(x)), 'someone else\'s browser source is never touched'); assert.ok(pressed.every(x => x.endsWith('|refreshnocache')), 'only page reloads, nothing else');
});

test('when everything is connected nothing is reloaded', async () => {
  const obs = await fakeObs(); cleanup.push(obs.close); const port = await freePort();
  const c = await startCore({ port, env: { IXC_EP_OBS_REVIVE_MS: '1500' }, config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  const player = await page(c, 'player', ['music.player', 'reload']), tts = await page(c, 'tts', ['tts.play', 'reload']), ov = await page(c, 'overlay', []); cleanup.push(() => player.close(), () => tts.close(), () => ov.close());
  await sleep(5000); assert.deepEqual(obs.st.pressed || [], [], 'no reload when the sources are already connected');
});
