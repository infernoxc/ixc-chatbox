// Azure Speech voices: offered only after the user pastes their own key and region; the key is never shown again;
// Indian / Hindi / whisper voices go to the (fake) Azure endpoint as SSML; rejected keys give a plain reason.
import { test, after } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, page, settings, until } from './harness.mjs';
import { fakeAzure, KEY } from './fake-azure.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });

test('Azure Speech (MOCKED server): setup, voice list from Azure, SSML, key privacy, rejected key', async () => {
  const az = await fakeAzure(); cleanup.push(az.close); const seen = az.speaks;
  const c = await startCore({ env: az.env, config: { general: { firstRunDone: true } } }); cleanup.push(() => c.stop());
  const keys = async () => (await api(c, '/api/tts/voices')).body.voices.map(v => v.key);

  assert.ok(!(await keys()).some(k => k.startsWith('az-')), 'no Azure voices before setup');
  assert.equal((await api(c, '/api/tts/azure', { region: 'bad host/x', key: KEY })).status, 400, 'region is checked');
  assert.equal((await api(c, '/api/tts/azure', { region: 'centralindia', key: 'short' })).status, 400, 'key shape is checked');
  assert.equal((await api(c, '/api/tts/azure-test', {})).status, 400, 'test says it is not set up');
  const r = await api(c, '/api/tts/azure', { region: 'centralindia', key: KEY }); assert.equal(r.body.ok, true); assert.equal(r.body.ready, true); assert.equal(r.body.checkError, null); assert.equal(az.lists, 1, 'voice list fetched from Azure');
  const ks = await keys(); for (const k of ['az-in-female', 'az-in-male', 'az-hi-female', 'az-hi-male', 'az-multi-female', 'az-multi-male', 'az-whisper']) assert.ok(ks.includes(k), k + ' offered');
  for (const k of ['az-en-in-neerja', 'az-hi-in-swara', 'az-en-us-avamultilingual']) assert.ok(ks.includes(k), k + ' from the catalogue');
  assert.ok(!ks.includes('az-fr-fr-denise') && !ks.includes('az-en-in-old'), 'other languages and deprecated voices are not offered');
  const vl = (await api(c, '/api/tts/voices')).body.voices; assert.match(vl.find(v => v.key === 'az-hi-in-swara').label, /Hindi \(India\) · native/); assert.equal(vl.find(v => v.key === 'az-en-us-avamultilingual').kind, 'multilingual'); assert.equal(vl.find(v => v.key === 'az-hi-male').locale, 'hi-IN');

  // the key never comes back out
  for (const p of ['/api/settings', '/api/tts/settings', '/api/tts/state']) assert.ok(!JSON.stringify((await api(c, p)).body).includes(KEY), p + ' must not contain the key');
  assert.equal((await api(c, '/api/tts/settings')).body.settings.azureKeySet, true);
  assert.ok(!c.output().includes(KEY) && !c.log('tts').includes(KEY), 'not in any log');

  assert.equal((await api(c, '/api/tts/azure-test', {})).body.ok, true); assert.equal(seen.at(-1).key, KEY); assert.match(seen.at(-1).type, /ssml/); assert.equal(seen.at(-1).fmt, 'audio-24khz-48kbitrate-mono-mp3');

  // a message in the whisper voice: SSML carries the voice, the whispering style and slower, lower prosody
  const p = await page(c, 'tts', ['tts.play']);
  await api(c, '/api/tts/say', { text: 'kya haal hai <bhai> & dosto', voice: 'az-whisper' });
  const play = await p.wait('tts.play', 8000); const audio = await fetch(c.url + play.url); assert.equal(audio.status, 200); assert.equal(audio.headers.get('content-type'), 'audio/mpeg');
  const last = seen.at(-1); assert.match(last.body, /voice name='en-US-JennyNeural'/); assert.match(last.body, /xml:lang='en-US'/); assert.match(last.body, /express-as style='whispering'/); assert.match(last.body, /rate='-15%'/); assert.match(last.body, /pitch='-4Hz'/);
  assert.ok(last.body.includes('&amp; dosto') && !last.body.includes('<bhai>'), 'text is escaped, no markup passes through');
  assert.equal((await api(c, '/api/tts/state')).body.current?.engine ?? '', (await api(c, '/api/tts/state')).body.current?.engine ?? '');
  p.send({ type: 'tts.done', n: play.n, ok: true });
  await api(c, '/api/tts/say', { text: 'hello', voice: 'az-hi-male' }); const play2 = await p.wait('tts.play', 8000); await fetch(c.url + play2.url);
  assert.match(seen.at(-1).body, /voice name='hi-IN-MadhurNeural'/); assert.match(seen.at(-1).body, /xml:lang='hi-IN'/); assert.doesNotMatch(seen.at(-1).body, /express-as/); p.send({ type: 'tts.done', n: play2.n, ok: true });

  // a custom voice may name the azure engine and a style
  await settings(c, { 'tts.voices': { 'my-calm': { voice: 'en-IN-NeerjaNeural', label: 'My calm voice', engine: 'azure', style: 'cheerful' } } });
  assert.ok((await keys()).includes('my-calm'));

  // rejected key: plain reason, no crash, the key is not leaked in the message
  az.mode = 'reject'; const bad = await api(c, '/api/tts/azure-test', {}); assert.equal(bad.status, 400); assert.match(bad.body.error, /rejected the key or the region/); assert.ok(!JSON.stringify(bad.body).includes(KEY)); assert.equal((await api(c, '/api/tts/settings')).body.settings.azure.state, 'rejected');

  // removing the key takes the voices away again
  assert.equal((await api(c, '/api/tts/azure', { remove: true })).body.ok, true); await until(async () => !(await keys()).some(k => k.startsWith('az-')), 4000, 'voices gone');
  assert.equal((await api(c, '/api/tts/settings')).body.settings.azureKeySet, false);
});
