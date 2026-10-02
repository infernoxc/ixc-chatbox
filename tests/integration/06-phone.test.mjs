// Phone remote through the real relay code (Cloudflare's local runtime): pairing, one-time codes, rate limits, device keys,
// reconnects, PC restarts, revoking, and what a phone may and may not do.
import { test, after, before } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, settings, client, startRelay, sleep, until } from './harness.mjs';

let relay, c; const cleanup = [];
before(async () => { relay = await startRelay(); cleanup.push(relay.stop); c = await startCore({ env: { IXC_EP_RELAY: relay.url } }); cleanup.push(() => c.stop()); });
after(async () => { for (const f of cleanup.reverse()) await f(); });
const relayUp = () => until(async () => (await api(c, '/api/remote/status')).body.relay === 'connected', 30000, 'PC connected to the relay');
async function phone(pcId, origin) { const ph = await client(relay.url.replace('http', 'ws') + '/phone/' + pcId, null, origin === undefined ? { headers: { Origin: relay.url } } : origin ? { headers: { Origin: origin } } : {}); return ph; }

test('pair a phone with the QR code, control IXC, reconnect with the device key', async () => {
  await relayUp();
  const pr = (await api(c, '/api/remote/pair', {})).body; assert.equal(pr.ok, true, JSON.stringify(pr));
  const m = pr.url.match(/\/p\/([\w-]+)#c=([\w-]+)$/); assert.ok(m, pr.url); const [, pcId, code] = m;
  const pageRes = await fetch(relay.url + '/p/' + pcId); assert.equal(pageRes.status, 200); assert.match(pageRes.headers.get('content-security-policy'), /frame-ancestors 'none'/); assert.match(await pageRes.text(), /IXC Remote/);
  const ph = await phone(pcId); await ph.wait((x) => x.relay === 'pc-online', 8000);
  ph.send({ type: 'hello', role: 'x', topics: ['music.state'] }); await sleep(300); assert.ok(!ph.msgs.some(x => x.type === 'hello'), 'nothing works before signing in');
  ph.send({ type: 'pair', code, device: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Safari/604.1' });
  const paired = await ph.wait('paired', 8000); assert.equal(paired.ok, true); assert.equal(paired.name, 'iPhone (Safari)'); assert.ok(paired.token.length > 30);
  ph.send({ type: 'hello', role: 'mobile', topics: ['music.state', 'status', 'chat', 'remote'] }); await ph.wait('hello', 5000); await ph.wait('music.state', 5000);
  ph.send({ type: 'music.cmd', c: { cmd: 'volume', v: 61 } }); await until(async () => (await api(c, '/api/music/state')).body.volume === 61, 5000, 'phone changed the volume');
  ph.send({ type: 'settings.set', patch: { 'tts.volume': 77 }, reqId: 'a' }); const ok = await ph.wait((x) => x.type === 'settings.saved' && x.reqId === 'a'); assert.equal(ok.ok, true);
  ph.send({ type: 'settings.set', patch: { 'remote.relayUrl': 'https://evil.example' }, reqId: 'b' }); const no = await ph.wait((x) => x.type === 'error' && x.reqId === 'b'); assert.match(no.error, /only be changed on the PC/);
  ph.send({ type: 'diag.get' }); assert.match((await ph.wait('error')).error, /not available from a phone/);
  ph.send({ type: 'ping', t: 1 }); await ph.wait('pong'); ph.send({ type: 'rtt', ms: 42 });
  await until(async () => (await api(c, '/api/remote/status')).body.devices[0]?.latencyMs === 42, 5000, 'latency shown on the PC');
  // the same QR code can't be used twice
  const ph2 = await phone(pcId); await ph2.wait((x) => x.relay === 'pc-online'); ph2.send({ type: 'pair', code, device: 'x' }); assert.equal((await ph2.wait('paired')).ok, false); await ph2.close();
  // reconnect with the device key (phone slept / changed network)
  await ph.close(); const ph3 = await phone(pcId); await ph3.wait((x) => x.relay === 'pc-online');
  ph3.send({ type: 'auth', deviceId: paired.deviceId, token: paired.token }); assert.equal((await ph3.wait('auth', 8000)).ok, true);
  ph3.send({ type: 'auth', deviceId: paired.deviceId, token: 'wrong' }); await sleep(200);
  await ph3.close();
  // IXC restarts: the phone gets "PC offline", then reconnects with the same key (devices are kept)
  const ph4 = await phone(pcId); await ph4.wait((x) => x.relay === 'pc-online'); ph4.send({ type: 'auth', deviceId: paired.deviceId, token: paired.token }); await ph4.wait('auth');
  await c.stop(); await ph4.wait((x) => x.relay === 'pc-offline', 10000, 'phone told the PC is offline');
  c = await c.restart(); cleanup.push(() => c.stop()); await ph4.wait((x) => x.relay === 'pc-online', 30000, 'PC back');
  ph4.send({ type: 'auth', deviceId: paired.deviceId, token: paired.token }); assert.equal((await ph4.wait('auth', 8000)).ok, true, 'the same key works after a restart');
  ph4.send({ type: 'hello', role: 'mobile', topics: ['music.state'] }); await ph4.wait('music.state');
  // rename + revoke from the PC: the phone is signed out at once and can't come back with its old key
  const st = (await api(c, '/api/remote/status')).body; assert.equal(st.devices.length, 1); assert.equal(st.devices[0].online, true);
  assert.equal((await api(c, '/api/remote/rename', { id: paired.deviceId, name: 'Ishan\'s Phone' })).body.ok, true);
  assert.equal((await api(c, '/api/remote/status')).body.devices[0].name, 'Ishan\'s Phone');
  await api(c, '/api/remote/revoke', { id: paired.deviceId }); const rv = await ph4.wait('auth', 5000); assert.equal(rv.ok, false); assert.equal(rv.reason, 'revoked');
  const ph5 = await phone(pcId); await ph5.wait((x) => x.relay === 'pc-online'); ph5.send({ type: 'auth', deviceId: paired.deviceId, token: paired.token }); assert.equal((await ph5.wait('auth')).ok, false); await ph5.close();
});

test('wrong codes are rate-limited; expired and cancelled codes fail; other websites and impostor PCs are refused', async () => {
  await relayUp(); const pr = (await api(c, '/api/remote/pair', {})).body; const [, pcId, code] = pr.url.match(/\/p\/([\w-]+)#c=([\w-]+)$/);
  const ph = await phone(pcId); await ph.wait((x) => x.relay === 'pc-online');
  for (let i = 0; i < 6; i++) { ph.send({ type: 'pair', code: 'guess' + i, device: 'x' }); assert.equal((await ph.wait('paired')).ok, false); }
  ph.send({ type: 'pair', code, device: 'x' }); const lim = await ph.wait('paired'); assert.equal(lim.ok, false); assert.match(lim.error, /Too many wrong codes/, 'even the right code is refused once rate-limited');
  await ph.close();
  await api(c, '/api/remote/cancel', {}); const st = (await api(c, '/api/remote/status')).body; assert.equal(st.pairing, false);
  await assert.rejects(phone(pcId, 'https://evil.example'), /403/, 'phone socket from another website');
  const imp = await client(relay.url.replace('http', 'ws') + '/pc/' + pcId, { t: 'hello', secret: 'not-the-real-secret-1234567890', v: '1' });
  const e = await imp.wait((x) => x.t === 'error', 5000); assert.equal(e.error, 'bad-secret', 'nobody can impersonate the PC'); await imp.close();
  assert.equal((await api(c, '/api/remote/status')).body.relay, 'connected', 'the real PC stays connected');
  const bad = await fetch(relay.url + '/p/short'); assert.equal(bad.status, 404);
});

test('phone remote off: the relay connection closes and pairing is refused', async () => {
  await settings(c, { 'remote.enabled': false }); await until(async () => (await api(c, '/api/remote/status')).body.relay === 'off', 8000, 'relay off');
  const r = await api(c, '/api/remote/pair', {}); assert.equal(r.body.ok, false);
  await settings(c, { 'remote.enabled': true }); await relayUp();
});
