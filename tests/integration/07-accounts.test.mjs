// Accounts: Twitch device sign-in, encrypted tokens, refresh on expiry, replies, follower events, viewers; Streamer.bot (optional).
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { startCore, api, settings, page, fakeTwitch, wsServer, httpServer, json, sleep, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });

test('Twitch: sign in with a code, reply in chat, follower alerts, viewer counts, token refresh', async () => {
  let polls = 0, tok = 'tok1', helix401 = true; const SC = ['chat:read', 'chat:edit', 'moderator:read:followers', 'channel:read:subscriptions'];
  const es = await wsServer((c) => { c.send({ metadata: { message_type: 'session_welcome' }, payload: { session: { id: 'sess1', keepalive_timeout_seconds: 10 } } });
    setTimeout(() => c.send({ metadata: { message_type: 'notification' }, payload: { subscription: { type: 'channel.follow' }, event: { user_id: '9', user_name: 'NewFan', user_login: 'newfan', followed_at: '2026-10-02T02:00:00Z' } } }), 1500); }); cleanup.push(es.close);
  const id = await httpServer((req, res, body) => {
    if (req.url === '/oauth2/device') { json(res, 200, { device_code: 'dev', user_code: 'ABCD-EFGH', verification_uri: 'https://www.twitch.tv/activate?device-code=ABCD', interval: 1, expires_in: 60 }); return; }
    if (req.url === '/oauth2/token') { const q = new URLSearchParams(body);
      if (q.get('grant_type') === 'refresh_token') { assert.equal(q.get('refresh_token'), 'ref1'); tok = 'tok2'; json(res, 200, { access_token: 'tok2', refresh_token: 'ref2', expires_in: 3600, scope: SC }); return; }
      if (++polls < 2) { json(res, 400, { status: 400, message: 'authorization_pending' }); return; } json(res, 200, { access_token: 'tok1', refresh_token: 'ref1', expires_in: 3600, scope: SC }); return; }
    if (req.url === '/oauth2/validate') { json(res, 200, { login: 'streamer', user_id: '100', scopes: SC }); return; }
    if (req.url === '/oauth2/revoke') { json(res, 200, {}); return; }
    res.writeHead(404); res.end(); }); cleanup.push(id.close);
  const helix = await httpServer((req, res) => {
    if (req.url.startsWith('/streams')) { if (helix401 && req.headers.authorization === 'Bearer tok1') { helix401 = false; json(res, 401, { message: 'Invalid OAuth token' }); return; } json(res, 200, { data: [{ viewer_count: 412, started_at: '2026-10-02T01:00:00Z' }] }); return; }
    if (req.url.startsWith('/eventsub/subscriptions')) { json(res, 202, { data: [{ id: 'sub' }] }); return; }
    if (req.url.startsWith('/channels/followers')) { json(res, 200, { data: req.url.includes('user_id=55') ? [{ user_id: '55' }] : [] }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(helix.close);
  const tw = await fakeTwitch(); cleanup.push(tw.close);
  const c = await startCore({ defaults: { twitchClientId: 'cid123' }, env: { IXC_EP_TWITCH_ID: id.url, IXC_EP_TWITCH_API: helix.url, IXC_EP_TWITCH_IRC: tw.url, IXC_EP_TWITCH_EVENTSUB: es.url }, config: { viewers: { refreshSec: 15 } } }); cleanup.push(() => c.stop());
  const p = await page(c, 'app', ['chat', 'accounts', 'alerts']);
  let acc = (await api(c, '/api/accounts')).body; assert.equal(acc.twitch.available, true); assert.equal(acc.youtube.available, false, 'YouTube sign-in needs the relay');
  const b = (await api(c, '/api/accounts/twitch/connect', {})).body; assert.equal(b.ok, true); assert.equal(b.userCode, 'ABCD-EFGH');
  await until(async () => { const t = (await api(c, '/api/accounts')).body.twitch; return t.flow?.state === 'connected' && t.account?.login === 'streamer'; }, 10000, 'signed in');
  // first sign-in also fills in the channel and turns Twitch on
  const v = (await api(c, '/api/settings')).body.values; assert.equal(v['platforms.twitch.channel'], 'streamer'); assert.equal(v['platforms.twitch.enabled'], true);
  const sec = fs.readFileSync(path.join(c.dir, 'secrets.dat')).toString('latin1'); assert.doesNotMatch(sec, /tok1|ref1/, 'tokens are stored encrypted');
  assert.doesNotMatch(fs.readFileSync(path.join(c.dir, 'config.json'), 'utf8'), /tok1|ref1/);
  await until(() => tw.live().find(x => x.pass === 'oauth:tok1' && x.nick === 'streamer' && x.chan === '#streamer'), 10000, 'IRC signed in');
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.twitch.canSend, 5000, 'can reply');
  const s = (await api(c, '/api/chat/send', { platform: 'twitch', message: 'hello chat' })).body; assert.equal(s.results[0].ok, true, JSON.stringify(s));
  await until(() => tw.live().some(x => x.lines.includes('PRIVMSG #streamer :hello chat')), 5000, 'message sent');
  const own = await p.wait((m) => m.type === 'chat.msg' && m.m.text === 'hello chat', 5000); assert.equal(own.m.me, true, 'your own reply shows in IXC');
  // follower alert from EventSub
  const f = await p.wait((m) => m.type === 'alert' && m.kind === 'follow', 10000); assert.equal(f.name, 'NewFan');
  // viewers: the first Helix call says 401 -> IXC refreshes the token and tries again
  await api(c, '/api/repair', { action: 'platform.twitch' });
  const vw = await until(async () => { const x = (await api(c, '/api/viewers')).body.platforms.twitch; return x.count === 412 ? x : null; }, 15000, 'Twitch viewers after refresh');
  assert.equal(tok, 'tok2', 'token was refreshed');
  // sign out: back to reading anonymously
  await api(c, '/api/accounts/twitch/signout', {}); await until(() => tw.live().find(x => /justinfan/.test(x.nick || '') && x.chan), 10000, 'anonymous again');
  acc = (await api(c, '/api/accounts')).body; assert.equal(acc.twitch.account, null);
  const s2 = (await api(c, '/api/chat/send', { platform: 'twitch', message: 'x' })).body; assert.equal(s2.results[0].ok, false); assert.match(s2.results[0].error, /Sign in/);
});

test('Streamer.bot (optional): password from its settings file, its chat for platforms IXC doesn\'t connect itself, replies through it', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-sb-')); fs.writeFileSync(path.join(dir, 'settings.json'), JSON.stringify({ websockets: { authPassword: 'sb-pass' } }));
  const { createHash } = await import('node:crypto'); const H = (x) => createHash('sha256').update(x).digest('base64');
  const sb = await wsServer((c) => {
    c.send({ request: 'Hello', info: { name: 'Streamer.bot' }, authentication: { salt: 's', challenge: 'c' } });
    c.ws.on('message', (d) => { const m = JSON.parse(d.toString()); c.lines.push(m);
      if (m.request === 'Authenticate') c.send({ id: m.id, status: m.authentication === H(H('sb-pass' + 's') + 'c') ? 'ok' : 'error', error: 'bad' });
      else if (m.request === 'Subscribe') { c.send({ id: m.id, status: 'ok' }); setTimeout(() => c.send({ event: { source: 'Kick', type: 'ChatMessage' }, data: { message: { msgId: 'sbk1', message: 'via streamer.bot', displayName: 'SbUser', username: 'sbuser' }, user: { name: 'SbUser', role: 1 } } }), 500); }
      else if (m.request === 'GetBroadcaster') c.send({ id: m.id, status: 'ok', platforms: { kick: { broadcastUserName: 'mykickname' } } });
      else if (m.request === 'SendMessage') c.send({ id: m.id, status: 'ok' });
      else if (m.request === 'GetCommands') c.send({ id: m.id, status: 'ok', commands: [{ enabled: true, commands: ['!lurk'] }] }); }); }); cleanup.push(sb.close);
  const c = await startCore({ env: { IXC_EP_STREAMERBOT: sb.url }, config: { platforms: { streamerbot: { mode: 'on', settingsPath: path.join(dir, 'settings.json') } } } }); cleanup.push(() => c.stop());
  const p = await page(c, 'chat', ['chat']);
  const m = await p.wait((x) => x.type === 'chat.msg' && x.m.id === 'sbk1', 10000); assert.equal(m.m.platform, 'kick'); assert.equal(m.m.via, 'streamerbot');
  const st = (await api(c, '/api/chat/status')).body.platforms.streamerbot; assert.equal(st.state, 'connected'); assert.equal(st.auth, 'ok');
  const s = (await api(c, '/api/chat/send', { platform: 'kick', message: 'hi from ixc' })).body; assert.equal(s.results[0].ok, true, JSON.stringify(s));
  assert.ok(sb.conns.some(x => x.lines.some(l => l.request === 'SendMessage' && l.platform === 'kick' && l.message === 'hi from ixc')));
  const sug = (await api(c, '/api/chat/suggest')).body; assert.ok(sug.commands.includes('!lurk') && sug.commands.includes('!np'), 'its commands + IXC\'s commands');
  assert.ok((await api(c, '/api/diag')).body.chat.yourAccounts.includes('mykickname'));
  // wrong password: shown clearly, chat stays isolated
  fs.writeFileSync(path.join(dir, 'settings.json'), JSON.stringify({ websockets: { authPassword: 'nope' } })); await api(c, '/api/repair', { action: 'streamerbot.reconnect' });
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.streamerbot.state === 'auth', 10000, 'auth problem shown');
});
