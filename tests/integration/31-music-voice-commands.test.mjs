// Chat commands for the music player and the chat voice: !shuffle !repeat !volume !previous !ttsvolume !ttsspeed !ttsvoice !ttspause
// !ttsresume !ttsclear. Moderators only by default; ready-made list adds them on request.
import { test, after } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, inject, page, settings, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const replies = (p, re) => p.msgs.filter(m => m.type === 'chat.reply' && re.test(m.text));
const val = async (c, k) => (await api(c, '/api/settings')).body.values[k];

test('music and voice commands: presets, permissions, numbers, toggles', async () => {
  const c = await startCore({ config: { general: { firstRunDone: true }, platforms: { streamerbot: { mode: 'off' } }, commands: { list: [{ trigger: '!np', response: 'Now playing: {song}', builtin: 'np' }] } } }); cleanup.push(() => c.stop());
  const pr = (await api(c, '/api/commands/presets')).body.presets; const want = ['!shuffle', '!repeat', '!volume', '!previous', '!ttsvolume', '!ttsspeed', '!ttsvoice', '!ttspause', '!ttsresume', '!ttsclear', '!skip', '!pause', '!resume'];
  for (const t of want) assert.ok(pr.find(x => x.trigger === t && !x.present), t + ' is offered');
  const r = await api(c, '/api/commands/presets', { triggers: want }); assert.deepEqual(r.body.added.sort(), want.slice().sort());
  const list = (await api(c, '/api/settings')).body.values['commands.list']; assert.deepEqual(list.find(x => x.trigger === '!previous').aliases, ['!prev']);
  assert.ok(list.filter(x => want.includes(x.trigger)).every(x => x.permission === 'moderator'), 'moderators only');
  await settings(c, { 'commands.list': list.map(x => ({ ...x, cooldownSec: 0 })), 'music.shuffle': false, 'music.repeat': true, 'music.volume': 40, 'tts.volume': 100, 'tts.speed': 0 });
  const p = await page(c, 'chat', ['chat']); let n = 0; const say = (name, text, extra = {}) => inject(c, { platform: 'kick', id: 'm' + (++n), name, text, ...extra });

  await say('Viewer', '!shuffle'); await say('Viewer', '!volume 90'); await say('Viewer', '!ttsvoice us-male');
  await new Promise(r => setTimeout(r, 400)); assert.equal(await val(c, 'music.shuffle'), false, 'a viewer can not change it'); assert.equal(await val(c, 'music.volume'), 40); assert.equal(replies(p, /./).length, 0);

  await say('Mod', '!shuffle', { mod: true }); await until(async () => (await val(c, 'music.shuffle')) === true, 4000, 'shuffle on'); await until(() => replies(p, /^Shuffle is on/).length === 1, 4000, 'said so');
  await say('Mod', '!shuffle off', { mod: true }); await until(async () => (await val(c, 'music.shuffle')) === false, 4000, 'shuffle off');
  await say('Mod', '!repeat off', { mod: true }); await until(async () => (await val(c, 'music.repeat')) === false, 4000, 'repeat off');
  await say('Mod', '!volume 70', { mod: true }); await until(async () => (await val(c, 'music.volume')) === 70, 4000, 'volume set'); await until(() => replies(p, /^Music volume is 70%/).length === 1, 4000, 'volume said');
  await say('Mod', '!volume +50', { mod: true }); await until(async () => (await val(c, 'music.volume')) === 100, 4000, 'clamped at 100');
  await say('Mod', '!volume -30%', { mod: true }); await until(async () => (await val(c, 'music.volume')) === 70, 4000, 'nudged down');
  await say('Mod', '!volume loud', { mod: true }); await until(() => replies(p, /Say a number/).length === 1, 4000, 'bad number refused'); assert.equal(await val(c, 'music.volume'), 70);
  await say('Mod', '!ttsvolume 55', { mod: true }); await until(async () => (await val(c, 'tts.volume')) === 55, 4000, 'tts volume');
  await say('Mod', '!ttsspeed -20', { mod: true }); await until(async () => (await val(c, 'tts.speed')) === -20, 4000, 'tts speed'); await say('Mod', '!ttsspeed 500', { mod: true }); await until(async () => (await val(c, 'tts.speed')) === 100, 4000, 'speed clamped');
  await say('Mod', '!ttspause', { mod: true }); await until(async () => (await val(c, 'tts.paused')) === true, 4000, 'tts paused'); await say('Mod', '!ttsresume', { mod: true }); await until(async () => (await val(c, 'tts.paused')) === false, 4000, 'tts resumed');
  await say('Mod', '!ttsvoice', { mod: true }); await until(() => replies(p, /^Voices: .*in-male/).length === 1, 4000, 'voices listed');
  await say('Mod', '!ttsvoice us-male', { mod: true }); await until(async () => (await val(c, 'tts.voice')) === 'us-male', 4000, 'voice picked');
  await say('Mod', '!ttsvoice nope-voice', { mod: true }); await until(() => replies(p, /No voice called/).length === 1, 4000, 'unknown voice');
  await say('Mod', '!ttsclear', { mod: true }); await until(() => replies(p, /queue cleared/).length === 1, 4000, 'cleared');
  await say('Mod', '!previous', { mod: true }); await say('Mod', '!prev', { mod: true }); await say('Mod', '!skip', { mod: true });   // nothing playing: must not crash the core
  assert.equal((await api(c, '/api/state')).status, 200);
});
