// Standalone (no Streamer.bot, no relay): the streamer's own developer apps entered in IXC. IDs are checked, secrets are stored
// encrypted and never shown again, and Twitch / Kick / YouTube sign-in and Kick replies work directly against the platforms
// (fake servers here).
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import http from 'node:http';
import { startCore, api, httpServer, wsServer, json, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const get = (c, p) => new Promise((res, rej) => http.get({ host: '127.0.0.1', port: c.port, path: p }, (r) => { let b = ''; r.on('data', d => b += d); r.on('end', () => res({ status: r.statusCode, body: b })); }).on('error', rej));

test('Your apps: own Twitch / Google / Kick apps entered in IXC; Kick sign-in and reply go straight to Kick (no relay, no Streamer.bot)', async () => {
  const KICK_SECRET = 'kick-secret-1234567890', GOOGLE_SECRET = 'GOCSPX-google-secret-xyz';
  let kickTokenForm = null, sent = [], googleForm = null;
  const kid = await httpServer((req, res, body) => {
    if (req.url === '/oauth/token') { kickTokenForm = new URLSearchParams(body); json(res, 200, { access_token: 'kick-tok', refresh_token: 'kick-ref', expires_in: 3600, scope: 'user:read channel:read chat:write' }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(kid.close);
  const kapi = await httpServer((req, res, body) => {
    if (req.url === '/users') { assert.equal(req.headers.authorization, 'Bearer kick-tok'); json(res, 200, { data: [{ user_id: 77, name: 'mykick' }] }); return; }
    if (req.url === '/chat' && req.method === 'POST') { assert.equal(req.headers.authorization, 'Bearer kick-tok'); sent.push(JSON.parse(body)); json(res, 200, { data: { is_sent: true } }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(kapi.close);
  const kweb = await httpServer((req, res) => { if (req.url.startsWith('/api/v2/channels/mykick')) json(res, 200, { id: 7, user_id: 77, chatroom: { id: 9 }, livestream: null }); else json(res, 404, {}); }); cleanup.push(kweb.close);
  const pusher = await wsServer((c) => { c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.2', activity_timeout: 120 }) }); }); cleanup.push(pusher.close);
  const gtok = await httpServer((req, res, body) => { googleForm = new URLSearchParams(body); json(res, 200, { access_token: 'g-tok', refresh_token: 'g-ref', expires_in: 3600, scope: 'https://www.googleapis.com/auth/youtube.force-ssl' }); }); cleanup.push(gtok.close);
  const gapi = await httpServer((req, res) => { if (req.url.startsWith('/channels')) json(res, 200, { items: [{ id: 'UCmine', snippet: { title: 'My Channel', customUrl: '@mine' } }] }); else json(res, 404, {}); }); cleanup.push(gapi.close);
  const c = await startCore({ env: { IXC_EP_KICK_ID: kid.url, IXC_EP_KICK_API: kapi.url, IXC_EP_KICK_WEB: kweb.url, IXC_EP_KICK_PUSHER: pusher.url + '/app/k', IXC_EP_GOOGLE_TOKEN: gtok.url, IXC_EP_GOOGLE_API: gapi.url, IXC_EP_YOUTUBE_WEB: 'http://127.0.0.1:1' },
    config: { platforms: { streamerbot: { mode: 'off' } } } }); cleanup.push(() => c.stop());
  // nothing built in, no relay: no platform can sign in yet, and Connect says what to do
  let acc = (await api(c, '/api/accounts')).body;
  for (const p of ['twitch', 'youtube', 'kick']) assert.equal(acc[p].available, false, p);
  assert.match((await api(c, '/api/accounts/kick/connect', {})).body.error, /Your apps/);
  // wrong-looking values are refused and nothing is saved
  let r = await api(c, '/api/accounts/apps', { twitch: { id: 'not a client id!' } }); assert.equal(r.status, 400); assert.match(r.body.error, /Client ID/);
  r = await api(c, '/api/accounts/apps', { google: { id: 'abc' } }); assert.equal(r.status, 400); assert.match(r.body.error, /googleusercontent/);
  assert.equal((await api(c, '/api/accounts')).body.apps.twitch.id, '');
  // save all three
  r = await api(c, '/api/accounts/apps', { twitch: { id: 'abcdefghij0123456789abcd' }, google: { id: '123-abc.apps.googleusercontent.com', secret: GOOGLE_SECRET }, kick: { id: '01ABCDEFGHJKMNPQRS', secret: KICK_SECRET } });
  assert.equal(r.status, 200, JSON.stringify(r.body));
  acc = (await api(c, '/api/accounts')).body;
  for (const p of ['twitch', 'youtube', 'kick']) assert.equal(acc[p].available, true, p + ' can sign in now');
  assert.equal(acc.apps.kick.secretSet, true); assert.equal(acc.apps.kick.redirect, 'http://localhost:' + c.port + '/oauth/kick');
  // secrets are never sent back, and never stored readable
  const everything = JSON.stringify(acc) + JSON.stringify((await api(c, '/api/settings')).body) + JSON.stringify((await api(c, '/api/diag')).body);
  for (const s of [KICK_SECRET, GOOGLE_SECRET]) { assert.ok(!everything.includes(s), 'secret leaked to the API');
    assert.ok(!fs.readFileSync(path.join(c.dir, 'secrets.dat')).toString('latin1').includes(s), 'secret readable on disk');
    assert.ok(!fs.readFileSync(path.join(c.dir, 'config.json'), 'utf8').includes(s), 'secret in config.json'); }
  for (const f of fs.readdirSync(path.join(c.dir, 'logs'))) { const t = fs.readFileSync(path.join(c.dir, 'logs', f), 'utf8'); for (const s of [KICK_SECRET, GOOGLE_SECRET]) assert.ok(!t.includes(s), 'secret in ' + f); }
  // Kick: PKCE sign-in, the browser comes back to IXC, IXC exchanges the code with Kick directly using the streamer's own secret
  const b = (await api(c, '/api/accounts/kick/connect', {})).body; assert.equal(b.ok, true, JSON.stringify(b));
  const u = new URL(b.url); assert.equal(u.searchParams.get('client_id'), '01ABCDEFGHJKMNPQRS'); assert.equal(u.searchParams.get('code_challenge_method'), 'S256');
  assert.equal(u.searchParams.get('redirect_uri'), 'http://localhost:' + c.port + '/oauth/kick');
  // a callback with the wrong state is refused
  let cb = await get(c, '/oauth/kick?code=abc&state=forged'); assert.match(cb.body, /state mismatch/); assert.equal(kickTokenForm, null);
  cb = await get(c, '/oauth/kick?code=abc&state=' + u.searchParams.get('state')); assert.match(cb.body, /Connected/);
  assert.equal(kickTokenForm.get('client_secret'), KICK_SECRET); assert.equal(kickTokenForm.get('client_id'), '01ABCDEFGHJKMNPQRS'); assert.equal(kickTokenForm.get('code'), 'abc'); assert.ok(kickTokenForm.get('code_verifier'));
  await until(async () => (await api(c, '/api/accounts')).body.kick.account?.login === 'mykick', 5000, 'signed in to Kick');
  // reply on Kick through Kick's official API
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.kick.canSend, 10000, 'Kick can send');
  r = (await api(c, '/api/chat/send', { platform: 'kick', message: 'hello from IXC' })).body; assert.equal(r.results[0].ok, true, JSON.stringify(r));
  assert.deepEqual(sent, [{ broadcaster_user_id: 77, content: 'hello from IXC', type: 'user' }]);
  assert.equal((await api(c, '/api/chat/status')).body.platforms.kick.sendVia, 'ixc', 'sent by IXC itself, not Streamer.bot');
  // Google: same, straight to Google with the streamer's own Desktop-app secret
  const g = (await api(c, '/api/accounts/youtube/connect', {})).body; const gu = new URL(g.url);
  assert.equal(gu.searchParams.get('redirect_uri'), 'http://127.0.0.1:' + c.port + '/oauth/youtube');
  cb = await get(c, '/oauth/youtube?code=gcode&state=' + gu.searchParams.get('state')); assert.match(cb.body, /Connected/);
  assert.equal(googleForm.get('client_secret'), GOOGLE_SECRET);
  await until(async () => (await api(c, '/api/accounts')).body.youtube.account?.displayName === 'My Channel', 5000, 'signed in to YouTube');
  // removing an app clears it
  r = await api(c, '/api/accounts/apps', { kick: { id: '', secret: '' } }); assert.equal(r.body.apps.kick.secretSet, false); assert.equal(r.body.apps.kick.own, false);
});
