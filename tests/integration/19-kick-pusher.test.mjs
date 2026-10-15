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
  await until(() => pusher.live().some(x => x.lines.includes('chatrooms.999.v2')), 20000, 'subscribed after the fix');
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.kick.state === 'connected', 5000, 'connected');
  const cfg = JSON.parse(fs.readFileSync(path.join(c.dir, 'config.json'), 'utf8'));
  assert.equal(cfg.platforms.kick.pusherKey, NEW); assert.equal(cfg.platforms.kick.pusherCluster, 'eu');
  assert.ok(!pusher.conns.some(x => x.req.url.includes('evil')), 'only Kick\'s own files are read');
});

test('Kick chat: kick.com unreadable -> the other Pusher clusters are tried', async () => {
  const { web, pusher } = await fakeKick(false, 'mt1', '32cbd69e4b950bf97679');
  const c = await startCore({ env: { IXC_EP_KICK_WEB: web.url, IXC_EP_KICK_PUSHER: pusher.url + '/{cluster}/app/{key}' }, config: { platforms: { streamerbot: { mode: 'off' }, kick: { enabled: true, channel: 'mykick' } } } });
  cleanup.push(() => c.stop());
  await until(() => pusher.live().some(x => x.lines.includes('chatrooms.999.v2')), 40000, 'found the right cluster');
  const tried = pusher.conns.map(x => x.req.url.split('/')[1]); assert.deepEqual(tried.slice(0, 4), ['us2', 'us3', 'us1', 'mt1']);
});
