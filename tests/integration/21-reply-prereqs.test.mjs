// Outgoing replies: every missing prerequisite is named per platform (app -> sign-in -> still valid), nothing is reported as sent
// unless the platform's API said so, and API errors / expired tokens / refresh failures are reported as they are. Fake servers only.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import http from 'node:http';
import { startCore, api, httpServer, wsServer, json, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const get = (c, p) => new Promise((res, rej) => http.get({ host: '127.0.0.1', port: c.port, path: p }, (r) => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const status = async (c) => (await api(c, '/api/chat/status')).body.platforms;
const send = async (c, platform, message = 'hi') => (await api(c, '/api/chat/send', { platform, message })).body.results;
const kickFakes = async () => {
  const kweb = await httpServer((req, res) => { if (req.url.startsWith('/api/v2/channels/mykick')) json(res, 200, { id: 7, user_id: 77, chatroom: { id: 9 }, livestream: null }); else json(res, 404, {}); }); cleanup.push(kweb.close);
  const pusher = await wsServer((c) => { c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.2', activity_timeout: 120 }) }); }); cleanup.push(pusher.close);
  return { kweb, pusher }; };

test('Replies: no apps / no sign-in -> each platform names its own missing step; nothing is claimed as sent', async () => {
  const { kweb, pusher } = await kickFakes();
  const c = await startCore({ env: { IXC_EP_KICK_WEB: kweb.url, IXC_EP_KICK_PUSHER: pusher.url + '/app/k', IXC_EP_YOUTUBE_WEB: 'http://127.0.0.1:1', IXC_EP_TWITCH_IRC: 'ws://127.0.0.1:1/' },
    config: { platforms: { streamerbot: { mode: 'off' }, twitch: { enabled: true, channel: 'mytwitch' }, kick: { enabled: true, channel: 'mykick' }, youtube: { enabled: true, channel: '@mine' } } } }); cleanup.push(() => c.stop());
  let st = await status(c);
  assert.match(st.twitch.sendNote, /add your Twitch app/); assert.match(st.kick.sendNote, /add your Kick app/); assert.match(st.youtube.sendNote, /add your Google \(YouTube\) app/);
  for (const p of ['twitch', 'kick', 'youtube']) assert.equal(st[p].canSend, false, p);
  let r = await send(c, 'all'); assert.equal(r.length, 1); assert.equal(r[0].ok, false);
  for (const re of [/Twitch app/, /Kick app/, /Google \(YouTube\) app/]) assert.match(r[0].error, re);
  r = await send(c, 'kick'); assert.equal(r[0].ok, false); assert.match(r[0].error, /add your Kick app/);
  // app saved, not signed in: the step changes to "connect your account"
  await api(c, '/api/accounts/apps', { twitch: { id: 'abcdefghij0123456789abcd' }, kick: { id: '01ABCDEFGHJKMNPQRS', secret: 'kick-secret-1234567890' } });
  st = await status(c); assert.match(st.twitch.sendNote, /connect your Twitch account/); assert.match(st.kick.sendNote, /connect your Kick account/); assert.doesNotMatch(st.kick.sendNote, /add your/);
  r = await send(c, 'twitch'); assert.equal(r[0].ok, false); assert.match(r[0].error, /connect your Twitch account/);
});

test('Kick replies: success only when Kick accepts; 403 and 429 are reported; an expired token that cannot refresh says so and sends nothing', async () => {
  let mode = 'ok', posts = 0, refreshes = 0, tokenExpiresIn = 3600;
  const kid = await httpServer((req, res, body) => { if (req.url === '/oauth/token') { if (/grant_type=refresh_token/.test(body)) { refreshes++; json(res, 400, { error: 'invalid_grant' }); return; }
    json(res, 200, { access_token: 'kick-tok', refresh_token: 'kick-ref', expires_in: tokenExpiresIn, scope: 'user:read channel:read chat:write' }); return; } res.writeHead(404); res.end(); }); cleanup.push(kid.close);
  const kapi = await httpServer((req, res) => {
    if (req.url === '/users') { json(res, 200, { data: [{ user_id: 77, name: 'mykick' }] }); return; }
    if (req.url === '/chat' && req.method === 'POST') { posts++; if (mode === 'ok') json(res, 200, { data: { is_sent: true } }); else if (mode === '403') json(res, 403, { message: 'missing scope chat:write' }); else json(res, 429, {}); return; }
    res.writeHead(404); res.end(); }); cleanup.push(kapi.close);
  const { kweb, pusher } = await kickFakes();
  const c = await startCore({ env: { IXC_EP_KICK_ID: kid.url, IXC_EP_KICK_API: kapi.url, IXC_EP_KICK_WEB: kweb.url, IXC_EP_KICK_PUSHER: pusher.url + '/app/k' },
    config: { platforms: { streamerbot: { mode: 'off' }, kick: { enabled: true, channel: 'mykick' } } } }); cleanup.push(() => c.stop());
  await api(c, '/api/accounts/apps', { kick: { id: '01ABCDEFGHJKMNPQRS', secret: 'kick-secret-1234567890' } });
  const connect = async () => { const b = (await api(c, '/api/accounts/kick/connect', {})).body; await get(c, '/oauth/kick?code=abc&state=' + new URL(b.url).searchParams.get('state')); };
  await connect(); await until(async () => (await status(c)).kick.canSend, 10000, 'Kick can send');
  assert.equal((await status(c)).kick.sendNote, '', 'no missing step once connected');
  let r = await send(c, 'kick', 'one'); assert.equal(r[0].ok, true); assert.equal(posts, 1);
  mode = '403'; r = await send(c, 'kick', 'two'); assert.equal(r[0].ok, false); assert.match(r[0].error, /^Kick: 403/); assert.match(r[0].error, /chat:write/);
  mode = '429'; r = await send(c, 'kick', 'three'); assert.equal(r[0].ok, false); assert.match(r[0].error, /too fast/);
  // a 1 s token: the next send tries to refresh, Kick refuses, the message is not sent and the account is marked expired
  tokenExpiresIn = 1; await connect(); await until(async () => (await api(c, '/api/accounts')).body.kick.account?.login === 'mykick', 5000, 'signed in with a 1 s token');
  mode = 'ok'; const before = posts; r = await send(c, 'kick', 'four');
  assert.equal(r[0].ok, false, JSON.stringify(r)); assert.equal(posts, before, 'nothing reached Kick'); assert.ok(refreshes >= 1, 'a refresh was tried');
  assert.equal((await api(c, '/api/accounts')).body.kick.account.invalid, true); assert.match((await status(c)).kick.sendNote, /Kick: .*(expired|connect)/);
  r = await send(c, 'all'); assert.equal(r[0].ok, false); assert.match(r[0].error, /Nothing was sent/);
});
