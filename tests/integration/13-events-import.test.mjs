// Event messages (thank-you on follow / sub / raid / Super Chat ...), different command answers per platform, and importing
// Streamer.bot's command names. Replies to test messages come back as "chat.reply" (nothing is sent to a real platform).
import { test, after } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, settings, inject, page, wsServer, sleep, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const replies = (p, re) => p.msgs.filter(m => m.type === 'chat.reply' && re.test(m.text));

test('Event messages: thank-you per event, on its own platform, with a cooldown against floods; off = silent', async () => {
  const c = await startCore({ config: { platforms: { streamerbot: { mode: 'off' } }, events: { cooldownSec: 2, follow: { on: true }, superchat: { on: true, message: 'Thanks {user} for {amount}!' }, raid: { on: false } } } }); cleanup.push(() => c.stop());
  const p = await page(c, 'chat', ['chat']);
  await inject(c, { platform: 'kick', id: 'f1', kind: 'follow', name: 'NewFan', text: 'followed' });
  await until(() => replies(p, /follow/).length === 1, 5000, 'follow thanked'); const r = replies(p, /follow/)[0]; assert.equal(r.text, 'Thanks for the follow, @NewFan!'); assert.equal(r.platform, 'kick');
  // a burst of follows within the cooldown: still one message
  for (let i = 2; i < 6; i++) await inject(c, { platform: 'kick', id: 'f' + i, kind: 'follow', name: 'Bot' + i, text: 'followed' });
  await sleep(800); assert.equal(replies(p, /follow/).length, 1, 'flood of follows -> one message');
  // a different platform has its own cooldown
  await inject(c, { platform: 'twitch', id: 'tf1', kind: 'follow', name: 'TwitchFan', text: 'followed' });
  await until(() => replies(p, /TwitchFan/).length === 1, 3000, 'twitch follow thanked');
  // after the cooldown, the next follower is thanked again
  await sleep(2100); await inject(c, { platform: 'kick', id: 'f9', kind: 'follow', name: 'Later', text: 'followed' }); await until(() => replies(p, /Later/).length === 1, 3000, 'thanked after cooldown');
  // custom message with {amount}
  await inject(c, { platform: 'youtube', id: 's1', kind: 'superchat', name: 'Big Fan', text: 'love it', amount: '₹500.00' });
  await until(() => replies(p, /Big Fan/).length === 1, 3000, 'superchat thanked'); assert.equal(replies(p, /Big Fan/)[0].text, 'Thanks @Big Fan for ₹500.00!');
  // raid message is off: nothing
  await inject(c, { platform: 'twitch', id: 'r1', kind: 'raid', name: 'Raider', text: 'raided' }); await sleep(800); assert.equal(replies(p, /Raider|raid/i).length, 0);
  // old alerts (history when IXC connects) are never thanked
  await inject(c, { platform: 'kick', id: 'f10', kind: 'follow', name: 'OldFollow', text: 'followed', old: true }); await sleep(500); assert.equal(replies(p, /OldFollow/).length, 0);
});

test('Commands: a different answer per platform; Streamer.bot import lists only commands IXC doesn\'t have yet', async () => {
  const sb = await wsServer((x) => { x.send({ request: 'Hello', info: { name: 'Streamer.bot' } });
    x.ws.on('message', (d) => { const m = JSON.parse(d.toString()); if (m.request === 'GetCommands') x.send({ id: m.id, status: 'ok', commands: [{ enabled: true, commands: ['!drop', '!discord'] }, { enabled: true, commands: ['!np'] }, { enabled: false, commands: ['!old'] }] }); else x.send({ id: m.id, status: 'ok', platforms: {} }); }); });
  cleanup.push(sb.close);
  const c = await startCore({ env: { IXC_EP_STREAMERBOT: sb.url }, config: { platforms: { streamerbot: { mode: 'off' } } } }); cleanup.push(() => c.stop());
  const p = await page(c, 'chat', ['chat']);
  // Streamer.bot not connected: the import says so
  let r = (await api(c, '/api/commands/streamerbot')).body; assert.equal(r.connected, false); assert.deepEqual(r.commands, []);
  await settings(c, { 'platforms.streamerbot.mode': 'on' });
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.streamerbot.state === 'connected', 10000, 'Streamer.bot connected');
  r = (await api(c, '/api/commands/streamerbot')).body; assert.deepEqual(r.commands, ['!discord', '!drop'], 'only commands IXC lacks, enabled ones');
  // add them with answers, one different on Kick
  const list = (await api(c, '/api/settings')).body.values['commands.list'];
  list.push({ trigger: '!discord', response: 'Join us: discord.gg/ixc', permission: 'everyone', cooldownSec: 0, platforms: [], enabled: true, aliases: [], responses: { kick: 'Kick fam, join us: discord.gg/ixc' } });
  list.push({ trigger: '!drop', response: '', permission: 'everyone', cooldownSec: 0, platforms: [], enabled: true, aliases: [], responses: { twitch: 'Drops are on!' } });
  let s = await api(c, '/api/settings', { patch: { 'commands.list': list } }); assert.equal(s.status, 200, JSON.stringify(s.body));
  // a command with no answer at all is refused
  s = await api(c, '/api/settings', { patch: { 'commands.list': list.concat([{ trigger: '!empty', response: '', permission: 'everyone', platforms: [], enabled: true, responses: {} }]) } }); assert.equal(s.status, 400); assert.match(s.body.error, /needs a response/);
  r = (await api(c, '/api/commands/streamerbot')).body; assert.deepEqual(r.commands, [], 'nothing left to import');
  // no Streamer.bot from here on: IXC answers by itself (with no account connected, the answer comes back to this test page)
  await settings(c, { 'platforms.streamerbot.mode': 'off' }); await until(async () => (await api(c, '/api/chat/status')).body.platforms.streamerbot.state !== 'connected', 5000, 'Streamer.bot off');
  await inject(c, { platform: 'twitch', id: 'd1', name: 'A', text: '!discord' }); await inject(c, { platform: 'kick', id: 'd2', name: 'B', text: '!discord' });
  await until(() => replies(p, /discord\.gg/).length === 2, 4000, 'both answered');
  const got = Object.fromEntries(replies(p, /discord\.gg/).map(m => [m.platform, m.text]));
  assert.equal(got.twitch, 'Join us: discord.gg/ixc'); assert.equal(got.kick, 'Kick fam, join us: discord.gg/ixc');
  await inject(c, { platform: 'twitch', id: 'd3', name: 'C', text: '!drop' }); await until(() => replies(p, /Drops are on/).length === 1, 3000, 'twitch-only answer');
  await inject(c, { platform: 'kick', id: 'd4', name: 'D', text: '!drop' }); await sleep(600); assert.equal(replies(p, /Drops/).length, 1, 'no answer where none is set');
});
