// Kick moved its chat to another Pusher key / cluster ("App key ... not in this cluster", code 4001): IXC reads the current
// settings from kick.com's own web app (fake), or tries the other clusters, reconnects and saves what works.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { startCore, api, httpServer, wsServer, json, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const NEW = '0123456789abcdef0123';
async function fakeKick(serveSite, goodCluster, goodKey) {
  const web = await httpServer((req, res) => {
    if (req.url.startsWith('/api/v2/channels/')) { json(res, 200, { id: 7, user_id: 77, chatroom: { id: 999 }, livestream: null }); return; }
    if (serveSite && req.url === '/') { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<html><script src="/_next/static/chunks/app-1.js"></script><script src="https://evil.example/x.js"></script></html>'); return; }
    if (serveSite && req.url === '/_next/static/chunks/app-1.js') { res.writeHead(200); res.end(`var a=1;window.p=new Pusher("${NEW}",{cluster:"eu",forceTLS:!0});`); return; }
    res.writeHead(403); res.end('blocked'); }); cleanup.push(web.close);
  const pusher = await wsServer((c) => {
    if (c.req.url.startsWith('/' + goodCluster + '/app/' + goodKey)) { c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.2', activity_timeout: 120 }) });
      c.ws.on('message', (d) => { const m = JSON.parse(d.toString()); if (m.event === 'pusher:subscribe') c.lines.push(m.data.channel); }); }
    else c.send({ event: 'pusher:error', data: { code: 4001, message: 'App key ' + c.req.url.split('/')[3] + ' not in this cluster. Did you forget to specify the cluster?' } }); }); cleanup.push(pusher.close);
  return { web, pusher };
}

test('Kick chat: new key and cluster read from kick.com, saved after it works', async () => {
  const { web, pusher } = await fakeKick(true, 'eu', NEW);
  const c = await startCore({ env: { IXC_EP_KICK_WEB: web.url, IXC_EP_KICK_PUSHER: pusher.url + '/{cluster}/app/{key}' }, config: { platforms: { streamerbot: { mode: 'off' }, kick: { enabled: true, channel: 'mykick' } } } });
  cleanup.push(() => c.stop());
  await until(() => pusher.live().some(x => x.lines.includes('chatrooms.999.v2')), 20000, 'subscribed after the fix').catch(e => { throw new Error(e.message + ' ' + fs.readFileSync(path.join(c.dir, 'logs', 'chat.log'), 'utf8').split('\n').filter(l => /Kick/.test(l)).slice(0, 8).join(' // ')); });
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.kick.state === 'connected', 5000, 'connected');
  const cfg = JSON.parse(fs.readFileSync(path.join(c.dir, 'config.json'), 'utf8'));
  assert.equal(cfg.platforms.kick.pusherKey, NEW); assert.equal(cfg.platforms.kick.pusherCluster, 'eu');
  assert.ok(!pusher.conns.some(x => x.req.url.includes('evil')), 'only Kick\'s own files are read');
});

test('Kick chat: new key by default; then the old key, then the other Pusher clusters', async () => {
  const { web, pusher } = await fakeKick(false, 'mt1', '34bf7a0ff419a2a775b9');
  const c = await startCore({ env: { IXC_EP_KICK_WEB: web.url, IXC_EP_KICK_PUSHER: pusher.url + '/{cluster}/app/{key}' }, config: { platforms: { streamerbot: { mode: 'off' }, kick: { enabled: true, channel: 'mykick' } } } });
  cleanup.push(() => c.stop());
  await until(() => pusher.live().some(x => x.lines.includes('chatrooms.999.v2')), 40000, 'found the right cluster');
  const tried = [...new Set(pusher.conns.map(x => x.req.url.split('?')[0].split('/').slice(1, 4).join('/')))]; assert.deepEqual(tried.slice(0, 5), ['us2/app/34bf7a0ff419a2a775b9', 'us2/app/32cbd69e4b950bf97679', 'us3/app/34bf7a0ff419a2a775b9', 'us1/app/34bf7a0ff419a2a775b9', 'mt1/app/34bf7a0ff419a2a775b9']);
});

test('Kick chat lifecycle: silent server times out and is retried, late refusal ignored, duplicates dropped, old sockets closed', async () => {
  let n = 0;
  const web = await httpServer((req, res) => { if (req.url.startsWith('/api/v2/channels/')) json(res, 200, { id: 7, user_id: 77, chatroom: { id: 999 }, livestream: null }); else { res.writeHead(403); res.end(); } }); cleanup.push(web.close);
  const pusher = await wsServer((c) => {
    if (++n === 1) return;   // 1st attempt: the server never answers
    c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.' + n, activity_timeout: 120 }) });
    c.ws.on('message', (d) => { const m = JSON.parse(d.toString()); if (m.event === 'pusher:subscribe') c.lines.push(m.data.channel); }); }); cleanup.push(pusher.close);
  const c = await startCore({ env: { IXC_EP_KICK_WEB: web.url, IXC_EP_KICK_PUSHER: pusher.url + '/{cluster}/app/{key}' }, config: { platforms: { streamerbot: { mode: 'off' }, kick: { enabled: true, channel: 'mykick' } } } });
  cleanup.push(() => c.stop());
  const live = await until(() => pusher.live().find(x => x.lines.includes('chatrooms.999.v2')), 30000, 'connected on the retry after the silent attempt');
  assert.equal(pusher.conns.length, 2, 'exactly one retry'); assert.equal(pusher.live().length, 1, 'the silent socket was closed');
  assert.match(pusher.conns[0].req.url, /\/us2\/app\/34bf7a0ff419a2a775b9/, 'the new key is used first');
  // a refusal arriving on the working connection must not tear it down or change the key
  live.send({ event: 'pusher:error', data: { code: 4001, message: 'App key 34bf7a0ff419a2a775b9 not in this cluster.' } });
  await new Promise(r => setTimeout(r, 3000));
  assert.equal(pusher.conns.length, 2, 'no reconnect after a late refusal'); assert.equal((await api(c, '/api/chat/status')).body.platforms.kick.state, 'connected');
  // the same message delivered twice shows once
  const { page } = await import('./harness.mjs'); const p = await page(c, 'chat', ['chat']);
  const msg = { event: 'App\\Events\\ChatMessageEvent', channel: 'chatrooms.999.v2', data: JSON.stringify({ id: 'dup-1', content: 'hello once', sender: { id: 5, username: 'Viewer', slug: 'viewer', identity: { badges: [] } } }) };
  live.send(msg); live.send(msg);
  await until(() => p.msgs.some(m => m.type === 'chat.msg' && m.m.text === 'hello once'), 5000, 'message shown');
  await new Promise(r => setTimeout(r, 800));
  assert.equal(p.msgs.filter(m => m.type === 'chat.msg' && m.m.text === 'hello once').length, 1, 'duplicate dropped'); await p.close();
});
