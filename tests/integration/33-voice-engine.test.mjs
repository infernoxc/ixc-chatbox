// Chat voice engine: Azure first, Windows voice as backup, in the SAME language only. All Azure answers here are MOCKED (fake-azure.mjs) and the
// Windows voices are stand-ins (IXC_SAPI_FAKE), so these tests prove IXC's choices, request contents and timing - not real audio quality.
// Part of IXC - (c) 2026 Ishan (InFerNoxC) - MIT License
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path';
import { startCore, api, page, settings, inject, sleep, until } from './harness.mjs';
import { fakeAzure, KEY } from './fake-azure.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
async function setup(sapi = 'en-IN:Heera:Female;en-US:Zira:Female', extra = {}) {
  const az = await fakeAzure(); cleanup.push(az.close);
  const c = await startCore({ env: Object.assign({}, az.env, { IXC_SAPI_FAKE: sapi }) }); cleanup.push(() => c.stop());
  await settings(c, Object.assign({ 'tts.azure.timeoutSec': 3, 'tts.azure.retries': 0, 'tts.azure.cooldownSec': 10 }, extra));
  assert.equal((await api(c, '/api/tts/azure', { region: 'centralindia', key: KEY })).body.ready, true);
  return { az, c }; }
const preview = async (c, text, voice) => (await api(c, '/api/tts/preview', { text, voice })).body;
const azState = async (c) => (await api(c, '/api/tts/settings')).body.settings.azure;

test('MOCKED: preview stays off stream; Azure errors fall back to a Windows voice, pause Azure, and Azure comes back', async () => {
  const { az, c } = await setup();
  let p = await preview(c, 'namaste dosto', 'az-in-female'); assert.equal(p.ok, true); assert.match(p.engine, /^Azure Speech \(en-IN-NeerjaNeural\)/); assert.equal(Buffer.from(p.audio, 'base64').length, 3000);
  const st = (await api(c, '/api/tts/state')).body; assert.equal(st.queue.length + st.history.length, 0, 'a preview is not queued or played in OBS');
  az.mode = 'down'; const n0 = az.speaks.length;
  p = await preview(c, 'first try', 'az-in-female'); assert.match(p.engine, /^Windows offline \(Heera\) - backup, online voice: Azure Speech error \(HTTP 500\)/);
  p = await preview(c, 'second try', 'az-in-female'); assert.match(p.engine, /Windows offline \(Heera\)/); assert.equal(az.speaks.length, n0 + 2, 'retries 0: one request per message');
  assert.equal((await azState(c)).state, 'paused', 'two failures in a row pause Azure');
  p = await preview(c, 'third try', 'az-in-female'); assert.match(p.engine, /Windows offline/); assert.equal(az.speaks.length, n0 + 2, 'no Azure request while paused');
  assert.match((await api(c, '/api/tts/state')).body.engine, /Using the Windows voice; trying Azure again/);
  az.mode = 'ok'; await sleep(10500);
  p = await preview(c, 'back again', 'az-in-female'); assert.match(p.engine, /^Azure Speech/, 'Azure is used again after the pause'); assert.equal((await azState(c)).state, 'ready');
  // retries: a network error is retried tts.azure.retries times, a refused request is not
  await settings(c, { 'tts.azure.retries': 2 }); az.mode = 'down'; const n1 = az.speaks.length; await preview(c, 'retry me', 'az-in-female'); assert.equal(az.speaks.length, n1 + 3);
});

test('MOCKED: timeout, unreachable network and cancelling a voice that is still being made', async () => {
  const { az, c } = await setup();
  az.mode = 'slow'; az.delay = 6000; const t0 = Date.now(); let p = await preview(c, 'slow one', 'az-hi-female');
  assert.ok(Date.now() - t0 < 5500, 'gives up after the 3 s timeout'); assert.match(p.error || '', /No Windows voice for Hindi/, 'Hindi is never read by an English Windows voice'); assert.match(p.error, /did not answer within 3 s/);
  // cancelling: skip while Azure is still answering aborts the request and does not count as an Azure failure
  az.mode = 'ok'; await api(c, '/api/tts/azure-test', {}); await settings(c, { 'tts.on': true, 'tts.azure.timeoutSec': 20 }); az.mode = 'slow'; az.delay = 8000;
  const tts = await page(c, 'tts', ['tts.play', 'tts']); const n0 = az.speaks.length, ab0 = az.aborted;
  await api(c, '/api/tts/say', { text: 'this one gets skipped', voice: 'az-in-male' }); await until(() => az.speaks.length > n0, 4000, 'request sent');
  await api(c, '/api/tts/skip', {}); await until(() => az.aborted === ab0 + 1, 3000, 'request aborted');
  await until(async () => (await api(c, '/api/tts/state')).body.history.some(h => h.status === 'skipped'), 3000, 'shown as skipped');
  await sleep(500); assert.ok(!(await api(c, '/api/tts/state')).body.history.some(h => /failed/.test(h.status)), 'not also marked failed');
  assert.ok(!tts.msgs.some(m => m.type === 'tts.play'), 'nothing played'); assert.equal((await azState(c)).state, 'ready', 'cancel is not an Azure failure');
  await tts.close();
  // nothing listening: plain reason
  await settings(c, { 'tts.engine': 'azure' }); await c.stop();
  const c2 = await startCore({ dir: c.dir, port: c.port, keepConfig: true, env: Object.assign({}, az.env, { IXC_EP_AZURE_TTS: 'http://127.0.0.1:1/' }) }); cleanup.push(() => c2.stop());
  p = await preview(c2, 'anyone there', 'az-in-male'); assert.equal(p.ok, false); assert.match(p.error, /could not be reached/); assert.doesNotMatch(p.error, new RegExp(KEY));
});

test('MOCKED: engine choice, missing Windows languages, Hindi script needs a Hindi voice', async () => {
  const { az, c } = await setup();
  await settings(c, { 'tts.engine': 'azure' }); az.mode = 'down';
  let p = await preview(c, 'azure only', 'az-in-female'); assert.equal(p.ok, false); assert.match(p.error, /HTTP 500/, 'Azure only: no Windows backup');
  await settings(c, { 'tts.engine': 'windows' }); az.mode = 'ok'; const n0 = az.speaks.length;
  p = await preview(c, 'windows only', 'az-in-female'); assert.match(p.engine, /^Windows offline \(Heera\)$/);
  p = await preview(c, 'hindi voice', 'az-hi-male'); assert.equal(p.ok, false); assert.match(p.error, /No Windows voice for Hindi \(hi-IN\) is installed\. Install the Hindi speech pack/);
  p = await preview(c, 'नमस्ते दोस्तों', 'az-in-female'); assert.match(p.error, /Hindi/, 'Devanagari text is not given to an English voice');
  p = await preview(c, 'any voice', 'local'); assert.match(p.engine, /Windows offline/);
  assert.equal(az.speaks.length, n0, 'Windows only: Azure is never called');
  const off = (await api(c, '/api/tts/settings')).body.settings.offline; assert.equal(off.hindi, false); assert.equal(off.indianEnglish, true); assert.match(off.message, /No Hindi Windows voice/);
  // Azure not set up but chosen: says so
  await api(c, '/api/tts/azure', { remove: true }); await settings(c, { 'tts.engine': 'azure' }); p = await preview(c, 'hello', 'in-male'); assert.match(p.error, /not set up/);

  const h = await setup('hi-IN:Kalpana:Female;en-IN:Ravi:Male', { 'tts.engine': 'windows' });
  p = await preview(h.c, 'नमस्ते दोस्तों', 'az-in-male'); assert.match(p.engine, /Windows offline \(Kalpana\)/);
  p = await preview(h.c, 'hello', 'az-in-female'); assert.match(p.engine, /Windows offline \(Ravi\)/, 'same language wins over gender');
});

test('MOCKED: Hindi Unicode, mixed text, punctuation, pronunciation list, long messages split at sentences', async () => {
  const { az, c } = await setup();
  await preview(c, 'नमस्ते दोस्तों, kya haal hai? Score 25-13 hai! ठीक है।', 'az-hi-female');
  const body = az.speaks.at(-1).body; assert.ok(body.includes('नमस्ते दोस्तों, kya haal hai? Score 25-13 hai! ठीक है।'), body); assert.match(body, /xml:lang='hi-IN'/);
  await settings(c, { 'tts.pronounce': ['gg=good game', 'भाई=भाई जी'] });
  await preview(c, 'gg bhai, GG! भाई आओ', 'az-in-female'); assert.ok(az.speaks.at(-1).body.includes('good game bhai, good game! भाई जी आओ'), az.speaks.at(-1).body);
  await settings(c, { 'tts.on': true, 'tts.maxChars': 500 }); const tts = await page(c, 'tts', ['tts.play', 'tts']); const n0 = az.speaks.length;
  const s1 = 'This is the first sentence of a long message and it keeps going for quite a while to be long enough.';
  const long = [s1, s1.replace('first', 'second'), s1.replace('first', 'third'), s1.replace('first', 'fourth'), 'Last one!'].join(' ');
  await api(c, '/api/tts/say', { text: long, voice: 'az-in-female' }); const play = await tts.wait('tts.play', 8000);
  const parts = az.speaks.slice(n0).map(x => x.body.match(/rate='[^']*'>(.*)<\/prosody>/)[1]);
  assert.ok(parts.length >= 2, 'split into ' + parts.length); assert.ok(parts.every(t => t.length <= 220)); assert.equal(parts.join(' '), long, 'nothing lost or reordered');
  assert.ok(parts.slice(0, -1).every(t => /[.!?]$/.test(t)), 'cut after sentence ends: ' + JSON.stringify(parts));
  assert.equal((await (await fetch(c.url + play.url)).arrayBuffer()).byteLength, 3000 * parts.length, 'audio parts joined');
  tts.send({ type: 'tts.done', n: play.n, ok: true });
  // one message at a time, and the same message mirrored on two platforms is read once
  await settings(c, { 'tts.readMode': 'all', 'tts.userCooldownSec': 0 });
  await inject(c, { platform: 'twitch', id: 'm1', name: 'Alex', text: 'same message everywhere' }); await inject(c, { platform: 'kick', id: 'm2', name: 'Alex', text: 'same message everywhere' });
  await inject(c, { platform: 'twitch', id: 'm3', name: 'Sam', text: 'second message' });
  const a = await tts.wait('tts.play', 8000); assert.match(a.text, /same message everywhere/); await sleep(800); assert.ok(!tts.msgs.some(m => m.type === 'tts.play'), 'the next waits until this one is done');
  tts.send({ type: 'tts.done', n: a.n, ok: true }); const b = await tts.wait('tts.play', 8000); assert.match(b.text, /second message/);
  tts.send({ type: 'tts.done', n: b.n, ok: true }); await sleep(800); assert.ok(!tts.msgs.some(m => m.type === 'tts.play'), 'mirrored message read once');
  await tts.close();
});

test('MOCKED: settings and key survive a restart, key never stored or shown in plain text, repeated playback stays bounded', async () => {
  const { az, c } = await setup();
  await settings(c, { 'tts.engine': 'windows', 'tts.azure.timeoutSec': 5, 'tts.voice': 'az-hi-female' }); const lists = az.lists;
  const c2 = await c.restart({ env: Object.assign({}, az.env, { IXC_SAPI_FAKE: 'en-IN:Heera:Female' }) }); cleanup.push(() => c2.stop());
  const s = (await api(c2, '/api/tts/settings')).body.settings; assert.equal(s.engine, 'windows'); assert.equal(s['azure.timeoutSec'], 5); assert.equal(s.voice, 'az-hi-female'); assert.equal(s.azureKeySet, true);
  assert.ok((await api(c2, '/api/tts/voices')).body.voices.some(v => v.key === 'az-hi-female'), 'voice list loaded from disk'); assert.equal(az.lists, lists, 'no extra Azure call at start');
  for (const f of fs.readdirSync(c2.dir)) { const p = path.join(c2.dir, f); if (fs.statSync(p).isFile()) assert.ok(!fs.readFileSync(p).toString('latin1').includes(KEY), f + ' must not hold the key in plain text'); }
  for (const p of ['/api/diag', '/api/settings', '/api/tts/state', '/api/tts/settings']) assert.ok(!JSON.stringify((await api(c2, p)).body).includes(KEY), p);
  await settings(c2, { 'tts.engine': 'auto', 'tts.on': true }); const tts = await page(c2, 'tts', ['tts.play']);
  const rss = () => fs.existsSync('/proc/' + c2.proc.pid + '/status') ? +fs.readFileSync('/proc/' + c2.proc.pid + '/status', 'utf8').match(/VmRSS:\s+(\d+)/)[1] / 1024 : 0;   // Linux only; elsewhere just runs the 50 messages
  const say = async (i) => { await api(c2, '/api/tts/say', { text: 'message number ' + i, voice: 'az-in-female' }); const m = await tts.wait((x) => x && x.type === 'tts.play' && x.text.endsWith('number ' + i), 8000); await fetch(c2.url + m.url); tts.send({ type: 'tts.done', n: m.n, ok: true }); };
  for (let i = 0; i < 10; i++) await say(i); const before = rss();
  for (let i = 10; i < 50; i++) await say(i); const grow = rss() - before;
  assert.ok(grow < 60, 'memory grew ' + grow.toFixed(1) + ' MB over 40 messages');
  const st = (await api(c2, '/api/tts/state')).body; assert.equal(st.queue.length, 0); assert.ok(st.history.length <= 15);
  await tts.close();
});
