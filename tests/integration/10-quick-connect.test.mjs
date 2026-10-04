// Quick connect: the phone remote with no setup. IXC starts Cloudflare's quick tunnel (a stand-in here), shows a QR for the
// temporary address, and through that address only the phone page and the phone socket exist. The real phone page connects
// in Chromium through the tunnel address. Stopping it (or IXC) disconnects the phones and forgets their temporary keys.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import http from 'node:http';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { startCore, api, inject, client, wsServer, until } from './harness.mjs';

const here = path.dirname(fileURLToPath(import.meta.url)); const FAKE = path.join(here, '..', 'fakes', 'cloudflared.mjs'); const HOST = 'ixc-test-tunnel.trycloudflare.com';
const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
// a request as it arrives from the tunnel: to 127.0.0.1, with the public host name
function viaTunnel(c, p, opts = {}) { return new Promise((res, rej) => { const r = http.request({ host: '127.0.0.1', port: c.port, path: p, method: opts.method || 'GET', headers: Object.assign({ Host: HOST + ':' + c.port, 'CF-Ray': '8c0ffee-AMS', 'CF-Connecting-IP': '203.0.113.9' }, opts.headers || {}) }, (resp) => {
  let b = ''; resp.on('data', d => b += d); resp.on('end', () => res({ status: resp.statusCode, headers: resp.headers, body: b })); }); r.on('error', rej); r.end(opts.body); }); }

test('Quick connect: one click opens a temporary link, the QR pairs a phone, only the phone page is reachable, stopping forgets the phone', async () => {
  const c = await startCore({ env: { IXC_CLOUDFLARED: FAKE } }); cleanup.push(() => c.stop());
  let st = (await api(c, '/api/remote/status')).body; assert.equal(st.configured, false, 'no relay set up'); assert.equal(st.quick.state, 'off'); assert.equal(st.quick.supported, true);
  // nothing answers on the tunnel address before Quick connect is on
  assert.equal((await viaTunnel(c, '/health')).status, 421);
  // "Show QR code": starts the tunnel, then gives the QR
  let r = (await api(c, '/api/remote/pair', {})).body; assert.equal(r.starting, true, JSON.stringify(r));
  await until(async () => (await api(c, '/api/remote/status')).body.quick.state === 'online', 15000, 'quick connect online');
  r = (await api(c, '/api/remote/pair', {})).body; assert.equal(r.ok, true, JSON.stringify(r)); assert.equal(r.mode, 'quick');
  const u = new URL(r.url); assert.equal(u.hostname, HOST); const id = u.pathname.split('/')[2]; const code = (u.hash.match(/c=([\w-]+)/) || [])[1]; assert.ok(id && code);
  // through the tunnel: the phone page and nothing else
  const pg = await viaTunnel(c, '/p/' + id); assert.equal(pg.status, 200); assert.match(pg.body, /IXC Remote/); assert.match(pg.headers['content-security-policy'], /frame-ancestors 'none'/);
  for (const p of ['/api/settings', '/api/remote/pair', '/app/', '/chat/chat.html', '/core/phone.html', '/p/wrongid', '/ws', '/oauth/kick']) assert.equal((await viaTunnel(c, p)).status, 404, p);
  assert.equal((await viaTunnel(c, '/api/remote/pair', { method: 'POST', body: '{}', headers: { 'Content-Type': 'application/json' } })).status, 404);
  // safety net: something that came through Cloudflare but names this PC as its host still never reaches the local API
  for (const h of ['localhost:' + c.port, '127.0.0.1:' + c.port]) assert.equal((await viaTunnel(c, '/api/settings', { headers: { Host: h } })).status, 421, h);
  const wsUrl = 'ws://127.0.0.1:' + c.port + '/phone/' + id; const headers = { Host: HOST + ':' + c.port };
  await assert.rejects(client(wsUrl, null, { headers, origin: 'https://evil.example' }), /403/, 'other websites are refused');
  // pair like the phone page does
  const ph = await client(wsUrl, null, { headers, origin: u.origin });
  await ph.wait((m) => m.relay === 'pc-online', 5000);
  ph.send({ type: 'settings.set', patch: { 'tts.on': true } }); await new Promise(res => setTimeout(res, 300));
  assert.notEqual((await api(c, '/api/settings')).body.values['tts.on'], true, 'nothing works before pairing');
  ph.send({ type: 'pair', code: 'wrong-code', device: 'Mozilla/5.0 (Linux; Android 14) Chrome/130' }); assert.equal((await ph.wait('paired')).ok, false);
  ph.send({ type: 'pair', code, device: 'Mozilla/5.0 (Linux; Android 14) Chrome/130' }); const pr = await ph.wait('paired'); assert.equal(pr.ok, true, JSON.stringify(pr));
  ph.send({ type: 'hello', role: 'mobile', topics: ['chat', 'viewers'] }); ph.send('{"t":"ping"}'); await ph.wait((m) => m.t === 'pong', 3000);
  await inject(c, { platform: 'kick', id: 'q1', name: 'Fan', text: 'hello phone' }); const m = await ph.wait((x) => x.type === 'chat.msg' && x.m.id === 'q1', 5000); assert.equal(m.m.text, 'hello phone');
  ph.send({ type: 'remote.pair' }); assert.match((await ph.wait('error')).error, /not available from a phone/, 'PC-only actions stay PC-only');
  st = (await api(c, '/api/remote/status')).body; const dev = st.devices.find(d => d.id === pr.deviceId); assert.ok(dev.online && dev.temp && dev.connection === 'Quick connect', JSON.stringify(st.devices));
  // the code was single use
  const ph2 = await client(wsUrl, null, { headers, origin: u.origin }); await ph2.wait((m) => m.relay === 'pc-online'); ph2.send({ type: 'pair', code }); assert.equal((await ph2.wait('paired')).ok, false); await ph2.close();
  // stop: the phone is disconnected and its temporary key is forgotten
  const closed = new Promise(res => ph.ws.once('close', res));
  await api(c, '/api/remote/quick/stop', {}); await closed;
  st = (await api(c, '/api/remote/status')).body; assert.equal(st.quick.state, 'off'); assert.equal(st.devices.length, 0);
  assert.equal((await viaTunnel(c, '/p/' + id)).status, 421, 'the old address is gone');
});

test('Quick connect: a tunnel that fails is reported clearly and can be retried', async () => {
  const c = await startCore({ env: { IXC_CLOUDFLARED: FAKE, IXC_FAKE_TUNNEL_FAIL: '1' } }); cleanup.push(() => c.stop());
  await api(c, '/api/remote/pair', {});
  await until(async () => (await api(c, '/api/remote/status')).body.quick.state === 'error', 15000, 'error shown');
  const st = (await api(c, '/api/remote/status')).body; assert.match(st.quick.message, /429|try again/i, st.quick.message);
  const h = (await api(c, '/api/health')).body.items.find(i => i.id === 'phone'); assert.equal(h.level, 'warn'); assert.equal(h.fix, 'open:#phone');
});

test('Quick connect: the real phone page connects through the tunnel address and shows chat with viewers', async () => {
  // Streamer.bot running (replies go through it; its commands show in the phone's "!" list)
  const sb = await wsServer((x) => { x.send({ request: 'Hello', info: { name: 'Streamer.bot' } });
    x.ws.on('message', (d) => { const m = JSON.parse(d.toString()); if (m.request === 'GetCommands') x.send({ id: m.id, status: 'ok', commands: [{ enabled: true, commands: ['!drop'] }] }); else x.send({ id: m.id, status: 'ok', platforms: {} }); }); }); cleanup.push(sb.close);
  const c = await startCore({ env: { IXC_CLOUDFLARED: FAKE, IXC_EP_STREAMERBOT: sb.url }, config: { platforms: { streamerbot: { mode: 'on' } } } }); cleanup.push(() => c.stop());
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.streamerbot.state === 'connected', 10000, 'streamer.bot connected');
  await api(c, '/api/remote/pair', {}); await until(async () => (await api(c, '/api/remote/status')).body.quick.state === 'online', 15000, 'online');
  const r = (await api(c, '/api/remote/pair', {})).body;
  const exe = fs.existsSync('/opt/pw-browsers/chromium') ? '/opt/pw-browsers/chromium' : undefined;
  const b = await chromium.launch({ executablePath: exe, args: ['--no-sandbox', '--no-proxy-server', '--host-resolver-rules=MAP ' + HOST + ' 127.0.0.1'] }); cleanup.push(() => b.close());
  const p = await b.newPage({ viewport: { width: 390, height: 844 }, isMobile: true }); const errors = []; p.on('pageerror', e => errors.push(e.message));
  await p.goto(r.url); await p.waitForSelector('#conn.ok', { timeout: 15000 });
  await inject(c, { platform: 'twitch', id: 'qp1', name: 'Rushkaa', text: 'gg from quick connect' });
  await p.click('#tabs button[data-t="chat"]'); await p.waitForSelector('text=gg from quick connect', { timeout: 8000 });
  assert.match(await p.locator('#vbar').innerText(), /watching/); assert.match(await p.locator('#vbar').innerText(), /1\s*chatting/);
  await p.fill('#cIn', '!'); await p.waitForSelector('#sugs.show >> text=!np', { timeout: 8000 }); await p.waitForSelector('#sugs.show >> text=!drop', { timeout: 8000 });
  assert.match(await p.locator('#sugs [data-c="!drop"]').innerText(), /Streamer\.bot/);
  await p.screenshot({ path: path.join(here, '..', 'ui', 'shots', '30-phone-quick-connect-chat.png') });
  assert.deepEqual(errors, []);
  const st = (await api(c, '/api/remote/status')).body; assert.equal(st.devices.filter(d => d.online && d.temp).length, 1);
});
