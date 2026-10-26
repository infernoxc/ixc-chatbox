// Send delivery + chat health. MOCK TESTS: fake Twitch IRC / Twitch auth / Kick servers only - no real platform is contacted.
// Proves: "sent" only when the platform confirms (Twitch USERSTATE, Kick API ok), refusals and silence are failures, ALL lists every
// set-up platform with its own result, missing accounts are marked "needs-account", connection state is separate from "not live".
import { test, after } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, page, fakeTwitch, httpServer, wsServer, json, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const SC = ['chat:read', 'chat:edit'];
const send = async (c, platform, message = 'hi') => (await api(c, '/api/chat/send', { platform, message })).body.results;

async function twitchSignedIn(extraConfig = {}, tokenHandler) {
  const id = await httpServer((req, res, body) => {
    if (req.url === '/oauth2/device') { json(res, 200, { device_code: 'dev', user_code: 'ABCD-EFGH', verification_uri: 'https://www.twitch.tv/activate', interval: 1, expires_in: 60 }); return; }
    if (req.url === '/oauth2/token') { if (tokenHandler) { const r = tokenHandler(new URLSearchParams(body)); if (r) { json(res, r.code, r.body); return; } } json(res, 200, { access_token: 'tokA', refresh_token: 'refA', expires_in: 3600, scope: SC }); return; }
    if (req.url === '/oauth2/validate') { json(res, 200, { login: 'streamer', user_id: '100', scopes: SC }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(id.close);
  const helix = await httpServer((req, res) => { if (req.url.startsWith('/streams')) { json(res, 200, { data: [] }); return; } res.writeHead(404); res.end(); }); cleanup.push(helix.close);
  const tw = await fakeTwitch(); cleanup.push(tw.close);
  const kweb = await httpServer((req, res) => { if (req.url.startsWith('/api/v2/channels/mykick')) json(res, 200, { id: 7, user_id: 77, chatroom: { id: 9 }, livestream: null }); else json(res, 404, {}); }); cleanup.push(kweb.close);
  const pusher = await wsServer((c) => { c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.2', activity_timeout: 120 }) }); }); cleanup.push(pusher.close);
  const c = await startCore({ defaults: { twitchClientId: 'cid123' }, env: { IXC_EP_TWITCH_ID: id.url, IXC_EP_TWITCH_API: helix.url, IXC_EP_TWITCH_IRC: tw.url, IXC_EP_KICK_WEB: kweb.url, IXC_EP_KICK_PUSHER: pusher.url + '/app/k', IXC_EP_YOUTUBE_WEB: 'http://127.0.0.1:1' },
    config: Object.assign({ viewers: { refreshSec: 15 }, platforms: { streamerbot: { mode: 'off' }, twitch: { enabled: true, channel: 'streamer' }, kick: { enabled: true, channel: 'mykick' } } }, extraConfig) }); cleanup.push(() => c.stop());
  await api(c, '/api/accounts/twitch/connect', {});
  await until(async () => (await api(c, '/api/accounts')).body.twitch.account?.login === 'streamer', 10000, 'signed in');
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.twitch.canSend, 8000, 'twitch can send');
  return { c, tw, id };
}

test('MOCK chat health: connected + not live + last message are separate facts', async () => {
  const { c, tw } = await twitchSignedIn(); const p = await page(c, 'chat', ['chat']);
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.twitch.viewers.state === 'offline', 15000, 'viewer check ran');
  let st = (await api(c, '/api/chat/status')).body.platforms.twitch;
  assert.equal(st.state, 'connected'); assert.equal(st.lastMessage, null, 'nothing received yet'); assert.equal(st.viewers.state, 'offline', 'channel not live must not mean disconnected');
  tw.msg({ 'display-name': 'Fan', id: 'm1', 'user-id': '5' }, 'fan', 'hello from a viewer');
  const m = await p.wait((x) => x.type === 'chat.msg' && x.m.text === 'hello from a viewer', 8000); assert.equal(m.m.platform, 'twitch');
  st = (await api(c, '/api/chat/status')).body.platforms.twitch; assert.equal(st.state, 'connected'); assert.ok(st.lastMessage > Date.now() - 20000, 'last message time is set');
  const k = (await api(c, '/api/chat/status')).body.platforms.kick; assert.equal(k.lastMessage, null, 'per platform, not shared');
});

test('MOCK Send: confirmed -> sent; silence -> not confirmed; refusal -> failed with Twitch\'s reason; missing account -> needs-account', async () => {
  const { c, tw } = await twitchSignedIn();
  let r = await send(c, 'twitch'); assert.equal(r[0].ok, true); assert.equal(r[0].kind, 'sent');
  tw.sendMode = 'silent'; r = await send(c, 'twitch', 'nobody answers'); assert.equal(r[0].ok, false); assert.equal(r[0].kind, 'failed'); assert.match(r[0].error, /did not confirm/);
  tw.sendMode = 'ratelimit'; r = await send(c, 'twitch', 'too fast'); assert.equal(r[0].ok, false); assert.match(r[0].error, /too quickly/);
  tw.sendMode = null;
  r = await send(c, 'kick'); assert.equal(r[0].ok, false); assert.equal(r[0].kind, 'needs-account'); assert.match(r[0].error, /Kick/);
  // ALL: every set-up platform has its own result
  r = await send(c, 'all', 'to everyone'); const by = Object.fromEntries(r.map(x => [x.platform, x]));
  assert.equal(by.twitch.ok, true); assert.equal(by.kick.ok, false); assert.equal(by.kick.kind, 'needs-account'); assert.equal(by.twitch.label, 'Twitch');
});
// token refresh / expired sign-in / API errors: covered by 21-reply-prereqs.test.mjs (also mock)
