// What a streamer without developer sign-in apps gets: replies through Streamer.bot for platforms IXC reads itself,
// every Streamer.bot command in the "!" list (pushed when Streamer.bot connects), and Twitch viewer numbers without signing in.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { startCore, api, page, wsServer, httpServer, json, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });

test('Kick read by IXC (no sign-in) + Streamer.bot connected: replies go through Streamer.bot; its commands reach the chat box', async () => {
  const pusher = await wsServer((c) => { c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.2', activity_timeout: 120 }) }); }); cleanup.push(pusher.close);
  const kweb = await httpServer((req, res) => { if (req.url.startsWith('/api/v2/channels/infernoxc')) json(res, 200, { id: 7, user_id: 8, chatroom: { id: 9 }, livestream: null }); else json(res, 404, {}); }); cleanup.push(kweb.close);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-sb2-')); fs.writeFileSync(path.join(dir, 'settings.json'), JSON.stringify({}));
  let sbUp = false;
  const sb = await wsServer((c) => { if (!sbUp) { c.ws.close(); return; }   // "not running yet" until the test starts it
    c.send({ request: 'Hello', info: { name: 'Streamer.bot' } });
    c.ws.on('message', (d) => { const m = JSON.parse(d.toString()); c.lines.push(m);
      if (m.request === 'Subscribe' || m.request === 'SendMessage') c.send({ id: m.id, status: 'ok' });
      else if (m.request === 'GetBroadcaster') c.send({ id: m.id, status: 'ok', platforms: {} });
      else if (m.request === 'GetCommands') c.send({ id: m.id, status: 'ok', commands: [{ enabled: true, commands: ['!drop', '!discord'] }, { enabled: false, commands: ['!off'] }, { enabled: true, commands: ['!socials'] }] }); }); }); cleanup.push(sb.close);
  const c = await startCore({ env: { IXC_EP_KICK_WEB: kweb.url, IXC_EP_KICK_PUSHER: pusher.url + '/app/k', IXC_EP_STREAMERBOT: sb.url },
    config: { platforms: { kick: { enabled: true, channel: 'infernoxc' }, streamerbot: { mode: 'on', settingsPath: path.join(dir, 'settings.json') } } } }); cleanup.push(() => c.stop());
  const chat = await page(c, 'chat', ['chat']);
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.kick.state === 'connected', 10000, 'kick connected');
  // before Streamer.bot runs: Kick can't be replied to, and the reason is clear
  let k = (await api(c, '/api/chat/status')).body.platforms.kick; assert.equal(k.canSend, false); assert.match(k.sendNote, /Sign in/);
  sbUp = true; await api(c, '/api/repair', { action: 'streamerbot.reconnect' });
  // Streamer.bot connects: Kick becomes sendable through it, and the chat box gets the full command list without asking
  await until(async () => { const s = (await api(c, '/api/chat/status')).body.platforms.kick; return s.canSend && s.sendVia === 'streamerbot'; }, 15000, 'kick sendable via Streamer.bot');
  const pushed = await chat.wait((m) => m.type === 'chat.suggest' && m.commands.includes('!drop'), 10000);
  for (const x of ['!drop', '!discord', '!socials', '!np', '!sr']) assert.ok(pushed.commands.includes(x), 'has ' + x);
  assert.ok(!pushed.commands.includes('!off'), 'disabled Streamer.bot commands are left out');
  assert.equal(pushed.from['!drop'], 'Streamer.bot'); assert.equal(pushed.from['!np'], 'IXC');
  // send to Kick, and to ALL
  let r = (await api(c, '/api/chat/send', { platform: 'kick', message: 'hello kick' })).body; assert.equal(r.results[0].ok, true, JSON.stringify(r));
  r = (await api(c, '/api/chat/send', { platform: 'all', message: 'hello everyone' })).body; assert.ok(r.results.some(x => x.platform === 'kick' && x.ok), JSON.stringify(r));
  const sent = sb.conns.flatMap(x => x.lines).filter(l => l.request === 'SendMessage' && l.platform === 'kick').map(l => l.message);
  assert.deepEqual(sent, ['hello kick', 'hello everyone']);
});

test('Twitch viewers without signing in (public data), offline shown as offline, a missing channel is reported', async () => {
  let live = true;
  const gql = await httpServer((req, res, body) => { assert.equal(req.headers['client-id'], 'kimne78kx3ncx6brgo4mv6wki5h1ko'); const login = (body.match(/login:\\"(\w+)\\"/) || [])[1];
    json(res, 200, [{ data: { user: login === 'nobody' ? null : { stream: live ? { viewersCount: 321, createdAt: '2026-10-02T01:00:00Z' } : null } } }]); }); cleanup.push(gql.close);
  const c = await startCore({ env: { IXC_EP_TWITCH_GQL: gql.url, IXC_EP_TWITCH_IRC: 'ws://127.0.0.1:1/' }, config: { viewers: { refreshSec: 15 }, platforms: { twitch: { enabled: true, channel: 'infernoxc00' } } } }); cleanup.push(() => c.stop());
  await until(async () => (await api(c, '/api/viewers')).body.platforms.twitch.count === 321, 15000, 'twitch viewers live');
  live = false; await api(c, '/api/settings', { patch: { 'platforms.twitch.channel': 'InfernoXC00' } });
  await until(async () => (await api(c, '/api/viewers')).body.platforms.twitch.state === 'offline', 15000, 'twitch offline');
  await api(c, '/api/settings', { patch: { 'platforms.twitch.channel': 'nobody' } });
  await until(async () => /no channel called/.test((await api(c, '/api/viewers')).body.platforms.twitch.note || ''), 15000, 'missing channel reported');
});
