// MOCKED (fake Kick chat socket, fake Twitch sign-in / Helix / EventSub): channel point rewards, sound effects, spoken alerts.
// Proves IXC's logic; a live claim on a real Twitch / Kick channel still has to be tried once by the streamer.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { startCore, api, settings, inject, page, wsServer, httpServer, json, until, sleep } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
function wav() { const n = 800, b = Buffer.alloc(44 + n); b.write('RIFF', 0); b.writeUInt32LE(36 + n, 4); b.write('WAVEfmt ', 8); b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(1, 22);
  b.writeUInt32LE(8000, 24); b.writeUInt32LE(8000, 28); b.writeUInt16LE(1, 32); b.writeUInt16LE(8, 34); b.write('data', 36); b.writeUInt32LE(n, 40); b.fill(128, 44);
  const f = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-snd-')), 'boom.wav'); fs.writeFileSync(f, b); return f; }
const queue = async (c) => (await api(c, '/api/tts/state')).body.queue.map(q => q.text);
const recent = async (c) => (await api(c, '/api/rewards')).body.recent;

test('Kick rewards (chat socket), sound effects, !sound, spoken alerts with a sound first, de-duplication, validation', async () => {
  const pusher = await wsServer((c) => { c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.2', activity_timeout: 120 }) });
    c.ws.on('message', (d) => { const m = JSON.parse(d.toString()); if (m.event === 'pusher:subscribe') c.lines.push(m.data.channel); }); }); cleanup.push(pusher.close);
  const kweb = await httpServer((req, res) => { if (req.url.startsWith('/api/v2/channels/my-chan')) json(res, 200, { id: 777, user_id: 888, chatroom: { id: 999 }, livestream: null }); else json(res, 404, {}); }); cleanup.push(kweb.close);
  const c = await startCore({ env: { IXC_EP_KICK_WEB: kweb.url, IXC_EP_KICK_PUSHER: pusher.url + '/app/k', IXC_EP_TWITCH_IRC: 'ws://127.0.0.1:1/' },
    config: { general: { firstRunDone: true }, platforms: { kick: { enabled: true, channel: 'My_Chan' }, streamerbot: { mode: 'off' } } } }); cleanup.push(() => c.stop());
  const file = wav();
  await settings(c, { 'sounds.list': [{ name: 'Boom', file, volume: 70, cooldownSec: 30 }] });
  const pre = (await api(c, '/api/rewards')).body.presets; assert.ok(pre.some(p => p.action === 'songrequest') && pre.some(p => p.arg === '!skip'), 'ready-made rewards offered');
  await settings(c, { 'rewards.list': pre.filter(p => ['Play a sound', 'Hydrate!', 'Read my message aloud'].includes(p.title)) });
  assert.equal((await api(c, '/api/settings')).body.values['sounds.list'][0].name, 'boom', 'sound names are lower-case');
  const p = await page(c, 'app', ['chat', 'alerts']); await until(() => pusher.live().length && pusher.live()[0].lines.includes('chatrooms.999.v2'), 8000, 'kick subscribed');
  const send = (data) => pusher.live()[0].send({ event: 'App\\Events\\RewardRedeemedEvent', channel: 'chatrooms.999.v2', data: JSON.stringify(data) });
  // a claim: shown in chat as an alert, then the sound is queued for the IXC TTS source
  send({ id: 'r1', reward_title: 'Play a sound', username: 'Viewer1', user_input: 'boom' });
  const a = await p.wait((m) => m.type === 'chat.msg' && m.m.kind === 'reward', 6000); assert.equal(a.m.name, 'Viewer1'); assert.match(a.m.text, /redeemed Play a sound: boom/);
  await until(async () => (await queue(c)).includes('(sound) boom'), 5000, 'sound queued');
  send({ id: 'r1', reward_title: 'Play a sound', username: 'Viewer1', user_input: 'boom' }); await sleep(800);
  assert.equal((await queue(c)).filter(t => t === '(sound) boom').length, 1, 'the same claim twice counts once');
  // the same claim from another tool (no id) right after: still once
  await api(c, '/api/rewards/redeem', { platform: 'kick', title: 'Play a sound', user: 'Viewer1', input: 'boom' }); await sleep(500);
  assert.equal((await queue(c)).filter(t => t === '(sound) boom').length, 1, 'a second source of the same claim counts once');
  // unknown sound / unknown reward: clear reason, nothing played
  await api(c, '/api/rewards/redeem', { platform: 'kick', title: 'Play a sound', user: 'Viewer2', input: 'nope' });
  await until(async () => (await recent(c)).some(r => r.user === 'Viewer2' && /no sound called nope/.test(r.result)), 5000, 'unknown sound reported');
  await api(c, '/api/rewards/redeem', { platform: 'kick', title: 'Free pizza', user: 'Viewer3' });
  await until(async () => (await recent(c)).some(r => r.title === 'Free pizza' && /not set up in IXC/.test(r.result)), 5000, 'unknown reward reported');
  // read a message aloud (Hindi + Hinglish pass through), message reward runs
  await api(c, '/api/rewards/redeem', { platform: 'kick', title: 'Read my message aloud', user: 'Rahul_99', input: 'नमस्ते bhai, GG!' });
  await until(async () => (await queue(c)).some(t => t.includes('says') && t.includes('नमस्ते bhai')), 5000, 'tts reward queued');
  await api(c, '/api/rewards/redeem', { platform: 'kick', title: 'hydrate!', user: 'Viewer4' });
  await until(async () => (await recent(c)).some(r => r.user === 'Viewer4' && r.result === 'done'), 5000, 'title matching ignores case');
  // chat command: !sound boom (cooldown applies to chat, not to paid claims), !sounds lists them
  const before = (await queue(c)).filter(t => t === '(sound) boom').length;
  await inject(c, { platform: 'kick', id: 'c1', name: 'Chatter', text: '!sound boom' }); await inject(c, { platform: 'kick', id: 'c2', name: 'Chatter2', text: '!sound boom' }); await sleep(500);
  assert.equal((await queue(c)).filter(t => t === '(sound) boom').length, before + 1, 'chat command plays once, then the cooldown');
  // spoken alerts: the sound first, then the voice; a burst is read once
  await settings(c, { 'alerts.speak': true, 'alerts.follow.sound': file });
  await inject(c, { platform: 'kick', id: 'f1', kind: 'follow', name: 'NewFan', text: 'followed' }); await inject(c, { platform: 'kick', id: 'f2', kind: 'follow', name: 'Bot2', text: 'followed' });
  const q = await until(async () => { const x = await queue(c); return x.includes('New Fan just followed. Thank you!') ? x : null; }, 5000, 'follow spoken');
  assert.ok(q.indexOf('(sound) follow alert') >= 0 && q.indexOf('(sound) follow alert') < q.indexOf('New Fan just followed. Thank you!'), 'alert sound plays before the voice');
  assert.ok(!q.some(t => /Bot 2/.test(t)), 'a burst of follows is read once');
  // the OBS player (IXC TTS source) gets the sound file itself
  const player = await page(c, 'tts', ['tts.play']); const pl = await player.wait((m) => m.type === 'tts.play', 6000);
  const au = await fetch(c.url + pl.url); assert.equal(au.status, 200); assert.ok(['audio/wav', 'audio/mpeg'].includes(au.headers.get('content-type')));
  if (/sound/.test(pl.text)) assert.equal(Buffer.from(await au.arrayBuffer()).subarray(0, 4).toString(), 'RIFF', 'the sound file is played as-is'); await player.close();
  // validation
  assert.equal((await api(c, '/api/settings', { patch: { 'rewards.list': [{ title: 'x'.repeat(46), action: 'none' }] } })).status, 400, 'Twitch title limit');
  assert.equal((await api(c, '/api/settings', { patch: { 'rewards.list': [{ title: 'Skip', action: 'command', arg: 'skip' }] } })).status, 400, 'a command needs !');
  assert.equal((await api(c, '/api/settings', { patch: { 'sounds.list': Array.from({ length: 21 }, (_, i) => ({ name: 's' + i, file })) } })).status, 400, 'at most 20 sounds');
  assert.equal((await api(c, '/api/settings', { patch: { 'sounds.list': [{ name: 'air horn', file }] } })).status, 400, 'a sound name has no spaces');
  await p.close();
});

test('Twitch rewards: created by IXC, claims from EventSub, a failed song request gives the points back, no channel points explained', async () => {
  const SC = ['chat:read', 'chat:edit', 'moderator:read:followers', 'channel:manage:redemptions']; let polls = 0, affiliate = true; const calls = [];
  const es = await wsServer((c) => { c.send({ metadata: { message_type: 'session_welcome' }, payload: { session: { id: 'sess1', keepalive_timeout_seconds: 10 } } });
    setTimeout(() => c.send({ metadata: { message_type: 'notification' }, payload: { subscription: { type: 'channel.channel_points_custom_reward_redemption.add' },
      event: { id: 'red1', user_id: '7', user_login: 'songfan', user_name: 'SongFan', user_input: 'some song', status: 'unfulfilled', reward: { id: 'tw-1', title: 'Request a song', cost: 500 } } } }), 2500); }); cleanup.push(es.close);
  const id = await httpServer((req, res, body) => {
    if (req.url === '/oauth2/device') { json(res, 200, { device_code: 'dev', user_code: 'ABCD-EFGH', verification_uri: 'https://www.twitch.tv/activate', interval: 1, expires_in: 60 }); return; }
    if (req.url === '/oauth2/token') { if (++polls < 2) { json(res, 400, { status: 400, message: 'authorization_pending' }); return; } json(res, 200, { access_token: 'tok1', refresh_token: 'ref1', expires_in: 3600, scope: SC }); return; }
    if (req.url === '/oauth2/validate') { json(res, 200, { login: 'streamer', user_id: '100', scopes: SC }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(id.close);
  const helix = await httpServer((req, res, body) => {
    if (req.url.startsWith('/eventsub/subscriptions')) { calls.push('sub ' + JSON.parse(body).type); json(res, 202, { data: [{ id: 's' }] }); return; }
    if (req.url.startsWith('/channel_points/custom_rewards/redemptions')) { calls.push(req.method + ' redemption ' + req.url + ' ' + body); json(res, 200, { data: [{}] }); return; }
    if (req.url.startsWith('/channel_points/custom_rewards')) { calls.push(req.method + ' reward ' + body);
      if (!affiliate) { json(res, 403, { error: 'Forbidden', status: 403, message: 'The broadcaster must have partner or affiliate status.' }); return; }
      if (req.method === 'GET') { json(res, 200, { data: [] }); return; } json(res, 200, { data: [{ id: 'tw-' + calls.filter(x => x.startsWith('POST reward')).length, title: JSON.parse(body).title }] }); return; }
    if (req.url.startsWith('/streams')) { json(res, 200, { data: [] }); return; }
    json(res, 200, { data: [] }); }); cleanup.push(helix.close);
  const c = await startCore({ defaults: { twitchClientId: 'cid123' }, env: { IXC_EP_TWITCH_ID: id.url, IXC_EP_TWITCH_API: helix.url, IXC_EP_TWITCH_IRC: 'ws://127.0.0.1:1/', IXC_EP_TWITCH_EVENTSUB: es.url },
    config: { general: { firstRunDone: true }, songRequests: { enabled: false }, platforms: { streamerbot: { mode: 'off' } } } }); cleanup.push(() => c.stop());
  await settings(c, { 'rewards.list': [{ title: 'Request a song', cost: 500, input: true, action: 'songrequest', platforms: ['twitch'] }, { title: 'Skip the song', cost: 900, action: 'command', arg: '!skip', platforms: ['twitch', 'kick'] }] });
  // not signed in yet: a plain reason
  let s = (await api(c, '/api/rewards/sync', {})).body; assert.equal(s.ok, false); assert.match(s.results.find(r => r.platform === 'twitch').error, /connect your Twitch account/);
  assert.match(s.results.find(r => r.platform === 'kick').error, /exactly these titles/, 'Kick without the permission: make them by title');
  assert.equal((await api(c, '/api/accounts/twitch/connect', {})).body.ok, true);
  await until(async () => (await api(c, '/api/accounts')).body.twitch.account?.login === 'streamer', 10000, 'signed in');
  s = (await api(c, '/api/rewards/sync', {})).body; assert.equal(s.results.find(r => r.platform === 'twitch').ok, true, JSON.stringify(s));
  const list = (await api(c, '/api/settings')).body.values['rewards.list']; assert.equal(list[0].twitchId, 'tw-1'); assert.equal(list[1].twitchId, 'tw-2', 'IXC keeps the Twitch reward ids');
  assert.ok(calls.some(x => x.startsWith('POST reward') && x.includes('"is_user_input_required":true') && x.includes('"cost":500')));
  // the claim comes from EventSub; song requests are off -> the points go back
  await until(() => calls.some(x => x === 'sub channel.channel_points_custom_reward_redemption.add'), 10000, 'subscribed to reward claims');
  const back = await until(() => calls.find(x => x.startsWith('PATCH redemption')), 15000, 'refund');
  assert.match(back, /reward_id=tw-1/); assert.match(back, /id=red1/); assert.match(back, /"status":"CANCELED"/);
  await until(async () => (await recent(c)).some(r => r.user === 'SongFan' && /turned off.*points given back/.test(r.result)), 5000, 'reported');
  // a channel without channel points: IXC keeps the rewards and says why
  affiliate = false; s = (await api(c, '/api/rewards/sync', {})).body; assert.match(s.results.find(r => r.platform === 'twitch').error, /Affiliate or Partner/);
  assert.equal((await api(c, '/api/settings')).body.values['rewards.list'].length, 2);
});
