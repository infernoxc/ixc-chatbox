// Moderation from IXC (delete / time out / ban) through each platform's official API, as the connected account. Fake Twitch
// (Helix), Kick (public API) and YouTube (Data API): proves the requests IXC sends and how it reports answers, not the real services.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import http from 'node:http';
import { startCore, api, inject, page, fakeTwitch, httpServer, wsServer, json, until, sleep } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const get = (c, p) => new Promise((res, rej) => http.get({ host: '127.0.0.1', port: c.port, path: p }, (r) => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const ALL = ['chat:read', 'chat:edit', 'moderator:read:followers', 'channel:read:subscriptions', 'moderator:manage:chat_messages', 'moderator:manage:banned_users'];

test('Moderation: Twitch delete / timeout / ban via Helix, Kick timeout / ban via its API, clear answers when not possible', async () => {
  let scopes = ALL, notMod = false; const helixCalls = [], kickCalls = [];
  const id = await httpServer((req, res) => {
    if (req.url === '/oauth2/device') { json(res, 200, { device_code: 'dev', user_code: 'ABCD', verification_uri: 'https://www.twitch.tv/activate', interval: 1, expires_in: 60 }); return; }
    if (req.url === '/oauth2/token') { json(res, 200, { access_token: 'tt', refresh_token: 'tr', expires_in: 3600, scope: scopes }); return; }
    if (req.url === '/oauth2/validate') { json(res, 200, { login: 'streamer', user_id: '100', scopes }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(id.close);
  const helix = await httpServer((req, res, body) => {
    if (req.url.startsWith('/moderation/')) { helixCalls.push({ m: req.method, url: req.url, body: body ? JSON.parse(body) : null }); if (notMod) { json(res, 403, { message: 'The user in moderator_id is not one of the broadcaster\'s moderators.' }); return; } res.writeHead(req.method === 'DELETE' ? 204 : 200, { 'Content-Type': 'application/json' }); res.end(req.method === 'DELETE' ? '' : JSON.stringify({ data: [{}] })); return; }
    if (req.url.startsWith('/eventsub/subscriptions')) { json(res, 202, { data: [{ id: 's' }] }); return; }
    json(res, 200, { data: [] }); }); cleanup.push(helix.close);
  const es = await wsServer((c) => c.send({ metadata: { message_type: 'session_welcome' }, payload: { session: { id: 's1', keepalive_timeout_seconds: 10 } } })); cleanup.push(es.close);
  const tw = await fakeTwitch(); cleanup.push(tw.close);
  const kid = await httpServer((req, res) => json(res, 200, { access_token: 'kt', refresh_token: 'kr', expires_in: 3600 })); cleanup.push(kid.close);
  const kapi = await httpServer((req, res, body) => { if (req.url === '/users') { json(res, 200, { data: [{ user_id: 77, name: 'mykick' }] }); return; }
    if (req.url === '/moderation/bans') { kickCalls.push(JSON.parse(body)); json(res, 200, { data: {} }); return; } json(res, 404, {}); }); cleanup.push(kapi.close);
  const kweb = await httpServer((req, res) => { if (req.url.startsWith('/api/v2/channels/mykick')) json(res, 200, { id: 7, user_id: 77, chatroom: { id: 9 }, livestream: null }); else json(res, 404, {}); }); cleanup.push(kweb.close);
  const pusher = await wsServer((c) => c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.2', activity_timeout: 120 }) })); cleanup.push(pusher.close);
  const c = await startCore({ env: { IXC_EP_TWITCH_ID: id.url, IXC_EP_TWITCH_API: helix.url, IXC_EP_TWITCH_IRC: tw.url, IXC_EP_TWITCH_EVENTSUB: es.url, IXC_EP_KICK_ID: kid.url, IXC_EP_KICK_API: kapi.url, IXC_EP_KICK_WEB: kweb.url, IXC_EP_KICK_PUSHER: pusher.url + '/app/k' },
    config: { platforms: { streamerbot: { mode: 'off' }, kick: { enabled: true, channel: 'mykick' } } } }); cleanup.push(() => c.stop());
  // nothing connected: no moderation offered, and asking says why
  let st = (await api(c, '/api/chat/status')).body.platforms; assert.deepEqual(st.kick.mod, { delete: false, timeout: false, ban: false });
  let r = await api(c, '/api/chat/moderate', { platform: 'kick', action: 'ban', userId: '5', login: 'x' }); assert.equal(r.status, 400); assert.match(r.body.error, /Connect your Kick account/);
  r = await api(c, '/api/chat/moderate', { platform: 'rumble', action: 'delete', id: 'x' }); assert.match(r.body.error, /Rumble doesn't let apps moderate/);
  // connect Twitch (own app) and Kick (own app)
  await api(c, '/api/accounts/apps', { twitch: { id: 'abcdefghij0123456789abcd' }, kick: { id: '01ABCDEFGHJKMNPQRS', secret: 'kick-secret-1234567890' } });
  assert.equal((await api(c, '/api/accounts/twitch/connect', {})).body.ok, true);
  await until(async () => (await api(c, '/api/accounts')).body.twitch.account?.login === 'streamer', 10000, 'twitch signed in');
  const ku = new URL((await api(c, '/api/accounts/kick/connect', {})).body.url); assert.match(ku.searchParams.get('scope'), /moderation:ban/, 'Kick sign-in asks for the moderation permission');
  await get(c, '/oauth/kick?code=x&state=' + ku.searchParams.get('state'));
  await until(async () => (await api(c, '/api/accounts')).body.kick.account?.login === 'mykick', 5000, 'kick signed in');
  st = (await api(c, '/api/chat/status')).body.platforms;
  assert.deepEqual(st.twitch.mod, { delete: true, timeout: true, ban: true }); assert.deepEqual(st.kick.mod, { delete: false, timeout: true, ban: true }, 'no single-message delete on Kick');
  // Twitch: delete a message, time out, ban - each exactly as Helix expects; the message leaves IXC's history
  const p = await page(c, 'chat', ['chat']);
  await inject(c, { platform: 'twitch', id: 'msg-1', name: 'Spammer', login: 'spammer', text: 'buy followers' });
  r = await api(c, '/api/chat/moderate', { platform: 'twitch', action: 'delete', id: 'msg-1', userId: '555', login: 'spammer' }); assert.equal(r.status, 200, JSON.stringify(r.body));
  assert.deepEqual(helixCalls[0], { m: 'DELETE', url: '/moderation/chat?broadcaster_id=100&moderator_id=100&message_id=msg-1', body: null });
  assert.ok(!(await api(c, '/api/chat/history')).body.messages.some(m => m.id === 'msg-1'), 'deleted message removed in IXC too');
  r = await api(c, '/api/chat/moderate', { platform: 'twitch', action: 'timeout', userId: '555', login: 'spammer', seconds: 600, reason: 'spam' }); assert.equal(r.status, 200);
  assert.deepEqual(helixCalls[1].body, { data: { user_id: '555', duration: 600, reason: 'spam' } }); assert.equal(helixCalls[1].url, '/moderation/bans?broadcaster_id=100&moderator_id=100');
  r = await api(c, '/api/chat/moderate', { platform: 'twitch', action: 'ban', userId: '555', login: 'spammer' }); assert.equal(r.status, 200); assert.deepEqual(helixCalls[2].body, { data: { user_id: '555' } }, 'a ban has no duration');
  // the platform says no: reported, not hidden
  notMod = true; r = await api(c, '/api/chat/moderate', { platform: 'twitch', action: 'ban', userId: '556', login: 'other' }); assert.equal(r.status, 400); assert.match(r.body.error, /isn't a moderator/); notMod = false;
  // Kick: timeout in minutes, ban without duration
  r = await api(c, '/api/chat/moderate', { platform: 'kick', action: 'timeout', userId: '900', login: 'kspam', seconds: 600 }); assert.equal(r.status, 200, JSON.stringify(r.body));
  assert.deepEqual(kickCalls[0], { broadcaster_user_id: 77, user_id: 900, duration: 10 });
  r = await api(c, '/api/chat/moderate', { platform: 'kick', action: 'ban', userId: '900', login: 'kspam', reason: 'bot' }); assert.deepEqual(kickCalls[1], { broadcaster_user_id: 77, user_id: 900, reason: 'bot' });
  r = await api(c, '/api/chat/moderate', { platform: 'kick', action: 'delete', id: 'k1' }); assert.equal(r.status, 400); assert.match(r.body.error, /isn't available/);
  // an old Twitch sign-in without the moderation permission: asked to sign in again, nothing sent
  scopes = ['chat:read', 'chat:edit']; await api(c, '/api/accounts/twitch/connect', {}); await until(async () => !(await api(c, '/api/accounts')).body.twitch.account.scopes.includes('moderator:manage:banned_users'), 10000, 'old scopes');
  const n = helixCalls.length; r = await api(c, '/api/chat/moderate', { platform: 'twitch', action: 'ban', userId: '557', login: 'x' }); assert.match(r.body.error, /Sign in to Twitch again/); assert.equal(helixCalls.length, n);
});

test('Moderation: YouTube delete via the Data API; a ban while not live says so', async () => {
  const calls = [];
  const gtok = await httpServer((req, res) => json(res, 200, { access_token: 'g', refresh_token: 'gr', expires_in: 3600 })); cleanup.push(gtok.close);
  const gapi = await httpServer((req, res) => { calls.push(req.method + ' ' + req.url);
    if (req.url.startsWith('/channels')) { json(res, 200, { items: [{ id: 'UCme', snippet: { title: 'Me' } }] }); return; }
    if (req.url.startsWith('/liveChat/messages') && req.method === 'DELETE') { res.writeHead(204); res.end(); return; } json(res, 404, {}); }); cleanup.push(gapi.close);
  const c = await startCore({ env: { IXC_EP_GOOGLE_TOKEN: gtok.url, IXC_EP_GOOGLE_API: gapi.url, IXC_EP_YOUTUBE_WEB: 'http://127.0.0.1:1' }, config: { platforms: { streamerbot: { mode: 'off' } } } }); cleanup.push(() => c.stop());
  await api(c, '/api/accounts/apps', { google: { id: '1234-abc.apps.googleusercontent.com', secret: 'GOCSPX-secret-1234567' } });
  const u = new URL((await api(c, '/api/accounts/youtube/connect', {})).body.url); await get(c, '/oauth/youtube?code=x&state=' + u.searchParams.get('state'));
  await until(async () => (await api(c, '/api/accounts')).body.youtube.account?.displayName === 'Me', 5000, 'signed in');
  let r = await api(c, '/api/chat/moderate', { platform: 'youtube', action: 'delete', id: 'LCC.abc123', userId: 'UCspam' }); assert.equal(r.status, 200, JSON.stringify(r.body));
  assert.ok(calls.includes('DELETE /liveChat/messages?id=LCC.abc123'));
  r = await api(c, '/api/chat/moderate', { platform: 'youtube', action: 'ban', userId: 'UCspam', login: 'spam' }); assert.equal(r.status, 400); assert.match(r.body.error, /not live/);
});
