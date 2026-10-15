// Timers: native repeating chat messages (replaces Streamer.bot timers). Kick is the receiving platform here (fake kick.com +
// Kick API, IXC signed in with the streamer's own Kick app). One "minute" is 0.5 s in this test.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import http from 'node:http';
import { startCore, api, inject, httpServer, wsServer, json, until, sleep } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const get = (c, p) => new Promise((res, rej) => http.get({ host: '127.0.0.1', port: c.port, path: p }, (r) => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));

async function kickWorld() {
  const w = { live: false, sent: [] };
  w.id = await httpServer((req, res) => { if (req.url === '/oauth/token') json(res, 200, { access_token: 'kt', refresh_token: 'kr', expires_in: 3600 }); else { res.writeHead(404); res.end(); } });
  w.api = await httpServer((req, res, body) => {
    if (req.url === '/users') { json(res, 200, { data: [{ user_id: 77, name: 'mykick' }] }); return; }
    if (req.url === '/chat') { w.sent.push({ at: Date.now(), text: JSON.parse(body).content }); json(res, 200, { data: { is_sent: true } }); return; }
    res.writeHead(404); res.end(); });
  w.web = await httpServer((req, res) => { if (req.url.startsWith('/api/v2/channels/mykick')) json(res, 200, { id: 7, user_id: 77, chatroom: { id: 9 }, livestream: w.live ? { viewer_count: 42, is_live: true, created_at: '2026-10-15T10:00:00Z' } : null }); else json(res, 404, {}); });
  w.pusher = await wsServer((c) => { c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.2', activity_timeout: 120 }) }); });
  for (const s of [w.id, w.api, w.web, w.pusher]) cleanup.push(s.close);
  w.env = { IXC_EP_KICK_ID: w.id.url, IXC_EP_KICK_API: w.api.url, IXC_EP_KICK_WEB: w.web.url, IXC_EP_KICK_PUSHER: w.pusher.url + '/app/k', IXC_EP_TIMERS_TICK_MS: '200', IXC_EP_TIMERS_MINUTE_MS: '500' };
  return w; }
async function signInKick(c) {
  await api(c, '/api/accounts/apps', { kick: { id: '01ABCDEFGHJKMNPQRS', secret: 'kick-secret-1234567890' } });
  const u = new URL((await api(c, '/api/accounts/kick/connect', {})).body.url); await get(c, '/oauth/kick?code=x&state=' + u.searchParams.get('state'));
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.kick.canSend, 10000, 'Kick can send'); }
const chat = async (c, n) => { for (let i = 0; i < n; i++) await inject(c, { platform: 'kick', id: 'm' + Math.random(), name: 'Viewer' + i, text: 'hello ' + i }); };
const timers = async (c) => (await api(c, '/api/timers')).body.timers;

test('Timers: interval, chat messages in between, live only, pause, Send now, no repeats after reconnect or restart', async () => {
  const w = await kickWorld();
  let c = await startCore({ env: w.env, config: { platforms: { streamerbot: { mode: 'off' }, kick: { enabled: true, channel: 'mykick' } }, timers: { gapSec: 60 } } }); cleanup.push(() => c.stop());
  await signInKick(c);
  // a bad timer is refused
  let r = await api(c, '/api/settings', { patch: { 'timers.list': [{ message: '  ', intervalMin: 2 }] } }); assert.equal(r.status, 400); assert.match(r.body.error, /needs a message/);
  r = await api(c, '/api/settings', { patch: { 'timers.list': [
    { id: 'follow', name: 'Follow', message: 'Follow me everywhere! {viewers} watching', intervalMin: 2, minLines: 2, liveOnly: true, platforms: ['kick'] },
    { id: 'paused', name: 'Paused one', message: 'never sent', intervalMin: 1, minLines: 0, liveOnly: false, enabled: false }] } });
  assert.equal(r.status, 200, JSON.stringify(r.body));
  // not live yet: nothing goes out, and the page says why
  await sleep(2500); assert.equal(w.sent.length, 0, 'nothing while offline');
  let t = await timers(c); assert.match(t.find(x => x.id === 'follow').status, /go live/); assert.equal(t.find(x => x.id === 'paused').status, 'paused');
  // live, but fewer chat messages than asked for since the last one: still waiting
  w.live = true; await api(c, '/api/repair', { action: 'platform.kick' });
  await until(async () => (await api(c, '/api/viewers')).body.platforms.kick.state === 'live', 10000, 'Kick live');
  await until(async () => /more chat message/.test((await timers(c)).find(x => x.id === 'follow').status), 5000, 'waits for chat');
  assert.equal(w.sent.length, 0);
  await chat(c, 2);
  await until(() => w.sent.length === 1, 5000, 'timer sent'); assert.equal(w.sent[0].text, 'Follow me everywhere! 42 watching');
  // interval and chat lines both start again: no second message without new chat, even long after the interval
  await sleep(2500); assert.equal(w.sent.length, 1, 'no repeat without new chat');
  // a reconnect doesn't send it again
  await api(c, '/api/repair', { action: 'platform.kick' }); await sleep(1500); assert.equal(w.sent.length, 1, 'no repeat after reconnect');
  await chat(c, 2); await until(() => w.sent.length === 2, 5000, 'second time after more chat');
  const lastBefore = (await timers(c)).find(x => x.id === 'follow').lastSent; assert.ok(lastBefore > 0);
  // restart IXC: the last send time is kept (no reset, no immediate repeat)
  c = await c.restart(); cleanup.push(() => c.stop());
  assert.equal((await timers(c)).find(x => x.id === 'follow').lastSent, lastBefore, 'last send time survives a restart');
  await sleep(1000); assert.equal(w.sent.length, 2, 'no repeat after restart');
  // Send now (ignores the interval) and paused timers
  r = await api(c, '/api/timers/send', { id: 'follow' }); assert.equal(r.body.ok, true, JSON.stringify(r.body)); assert.deepEqual(r.body.sentTo, ['kick']);
  await until(() => w.sent.length === 3, 3000, 'sent by hand');
  assert.ok(!w.sent.some(x => x.text === 'never sent'), 'a paused timer never posts');
  // turning all timers off stops them
  await api(c, '/api/settings', { patch: { 'timers.enabled': false } }); await chat(c, 3); await sleep(2000); assert.equal(w.sent.length, 3);
  assert.equal((await timers(c)).find(x => x.id === 'follow').status, 'timers are off');
});

test('Timers: two timers due at once never post back to back (minimum gap)', async () => {
  const w = await kickWorld(); w.live = true;
  const c = await startCore({ env: w.env, config: { platforms: { streamerbot: { mode: 'off' }, kick: { enabled: true, channel: 'mykick' } }, timers: { gapSec: 120,
    list: [{ id: 'a', message: 'timer A', intervalMin: 1, minLines: 0, liveOnly: false }, { id: 'b', message: 'timer B', intervalMin: 1, minLines: 0, liveOnly: false }] } } }); cleanup.push(() => c.stop());
  await signInKick(c);
  await until(() => w.sent.length >= 2, 10000, 'both timers sent');
  const [x, y] = w.sent; assert.notEqual(x.text, y.text); assert.ok(y.at - x.at >= 900, 'at least the gap (1 s here) between them: ' + (y.at - x.at) + ' ms');
});
