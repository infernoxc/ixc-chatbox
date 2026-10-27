// Multistream destinations (registry + reachability check). Fake RTMP servers on localhost: a correct handshake, a server that drops
// the connection, a server that is not RTMP, one that never answers. Stream keys are write-only: never returned, logged or stored readable.
// Not covered here (not built / not testable offline): forwarding the stream, rtmps:// against a real certificate, real platform ingest servers.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import net from 'node:net'; import fs from 'node:fs'; import path from 'node:path';
import { startCore, api, until, freePort, sleep } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const KEY = 'live_123456789_SuperSecretStreamKey';
const ENV = { IXC_EP_MULTISTREAM_RETRY_MS: '100', IXC_EP_MULTISTREAM_TIMEOUT_MS: '1200', IXC_EP_MULTISTREAM_ATTEMPTS: '3' };
// behaviour: 'ok' = real RTMP handshake; 'drop' = close at once; 'http' = answers like a web server; 'silent' = never answers; a function(n) picks per connection number
async function rtmpServer(behaviour) {
  const port = await freePort(); const seen = { conns: 0, bytes: 0, data: [] }; const socks = new Set();
  const srv = net.createServer((s) => { socks.add(s); s.on('close', () => socks.delete(s)); s.on('error', () => {}); const n = ++seen.conns; const mode = typeof behaviour === 'function' ? behaviour(n) : behaviour; let buf = Buffer.alloc(0);
    s.on('data', (d) => { seen.bytes += d.length; seen.data.push(d); buf = Buffer.concat([buf, d]);
      if (mode === 'drop') { s.destroy(); return; }
      if (mode === 'http') { s.end('HTTP/1.1 400 Bad Request\r\n\r\n'); return; }
      if (mode === 'ok' && buf.length >= 1537) { const c1 = buf.subarray(1, 1537); s.write(Buffer.concat([Buffer.from([3]), Buffer.alloc(1536, 7), c1])); } }); });
  await new Promise(r => srv.listen(port, '127.0.0.1', r));
  const close = () => new Promise(r => { for (const s of socks) s.destroy(); srv.close(r); }); cleanup.push(close);
  return { port, url: 'rtmp://127.0.0.1:' + port + '/live', seen, close }; }
const dest = async (c, body) => (await api(c, '/api/multistream/save', body));
const list = async (c) => (await api(c, '/api/multistream')).body;
const one = async (c, id) => (await list(c)).destinations.find(d => d.id === id);
const check = async (c, id) => api(c, '/api/multistream/check', { id });
const settled = (c, id, ms = 15000) => until(async () => { const d = await one(c, id); return d && (d.state === 'ok' || d.state === 'failed') ? d : null; }, ms, 'check finished');

test('Multistream destinations: saved with an encrypted write-only key, validated, limited to 8, kept across a restart', async () => {
  const c = await startCore({ env: ENV }); cleanup.push(() => c.stop());
  let s = await list(c); assert.equal(s.max, 8); assert.deepEqual(s.destinations, []); assert.equal(s.relay.ready, true); assert.equal(s.relay.running, false, 'the relay only listens after it is started'); assert.equal(s.relay.streamKey, '', 'no ingest key shown while it is off');
  assert.ok(s.presets.some(p => p.platform === 'twitch' && p.url === 'rtmp://live.twitch.tv/app'));
  // bad addresses and keys are refused and nothing is saved
  for (const url of ['http://example.com/live', 'rtmp://user:pass@example.com/live', 'rtmp://example.com/live?key=abc', 'not a url', '']) { const r = await dest(c, { platform: 'custom', url, key: KEY }); assert.equal(r.status, 400, url); assert.equal(r.body.ok, false); }
  let r = await dest(c, { platform: 'custom', url: 'rtmp://example.com/live', key: 'has a space' }); assert.equal(r.status, 400);
  assert.equal((await list(c)).destinations.length, 0);
  r = await dest(c, { platform: 'twitch', url: 'rtmp://live.twitch.tv/app', key: KEY }); assert.equal(r.status, 200, JSON.stringify(r.body));
  const id = r.body.destination.id; assert.equal(r.body.destination.keySet, true); assert.equal(r.body.destination.name, 'Twitch'); assert.equal(r.body.destination.state, 'idle');
  // the key is never sent back, shown in settings/diagnostics, written in the config, the logs or (readable) on disk
  const everything = JSON.stringify(r.body) + JSON.stringify(await list(c)) + JSON.stringify((await api(c, '/api/settings')).body) + JSON.stringify((await api(c, '/api/diag')).body);
  assert.ok(!everything.includes(KEY), 'key leaked through the API');
  assert.ok(!fs.readFileSync(path.join(c.dir, 'secrets.dat')).toString('latin1').includes(KEY), 'key readable in secrets.dat');
  assert.ok(!fs.readFileSync(path.join(c.dir, 'config.json'), 'utf8').includes(KEY), 'key in config.json');
  for (const f of fs.readdirSync(path.join(c.dir, 'logs'))) assert.ok(!fs.readFileSync(path.join(c.dir, 'logs', f), 'utf8').includes(KEY), 'key in log ' + f);
  // editing without a key keeps the saved one; an explicit clear removes it
  r = await dest(c, { id, name: 'My Twitch', url: 'rtmp://live-fra.twitch.tv/app' }); assert.equal(r.body.destination.keySet, true); assert.equal(r.body.destination.name, 'My Twitch');
  r = await dest(c, { id, clearKey: true }); assert.equal(r.body.destination.keySet, false);
  await dest(c, { id, key: KEY });
  // up to 8
  for (let i = 1; i < 8; i++) assert.equal((await dest(c, { platform: 'custom', name: 'D' + i, url: 'rtmp://127.0.0.1:1/live' + i })).status, 200);
  r = await dest(c, { platform: 'custom', url: 'rtmp://127.0.0.1:1/ninth' }); assert.equal(r.status, 400); assert.match(r.body.error, /up to 8/);
  assert.equal((await list(c)).destinations.length, 8);
  // survives a restart, key still there (and still hidden)
  const c2 = await c.restart(); cleanup.push(() => c2.stop());
  s = await list(c2); assert.equal(s.destinations.length, 8); assert.equal(s.destinations.find(d => d.id === id).keySet, true); assert.equal(s.destinations.find(d => d.id === id).name, 'My Twitch');
  assert.equal((await api(c2, '/api/multistream/delete', { id })).status, 200); assert.equal((await list(c2)).destinations.length, 7); assert.equal((await api(c2, '/api/multistream/delete', { id })).status, 404);
});

test('Multistream check: a good server is reachable (no key, no video sent); failures are named; retries are bounded; a server that recovers is ok', async () => {
  const good = await rtmpServer('ok'), drop = await rtmpServer('drop'), web = await rtmpServer('http'), silent = await rtmpServer('silent');
  const flaky = await rtmpServer((n) => n <= 2 ? 'drop' : 'ok');
  const deadPort = await freePort();
  const c = await startCore({ env: ENV }); cleanup.push(() => c.stop());
  const add = async (name, url) => (await dest(c, { platform: 'custom', name, url, key: KEY })).body.destination.id;
  const gId = await add('good', good.url), dId = await add('drop', drop.url), wId = await add('web', web.url), sId = await add('silent', silent.url), fId = await add('flaky', flaky.url), xId = await add('dead', 'rtmp://127.0.0.1:' + deadPort + '/live');
  assert.equal((await check(c, gId)).status, 202); let d = await settled(c, gId); assert.equal(d.state, 'ok', d.detail); assert.equal(d.attempt, 1);
  assert.equal(good.seen.bytes, 1537, 'only the 1537-byte RTMP handshake was sent'); assert.ok(!Buffer.concat(good.seen.data).includes(KEY), 'the stream key was not sent');
  await check(c, dId); d = await settled(c, dId); assert.equal(d.state, 'failed'); assert.match(d.detail, /closed|reset|forcibly|handshake/i); assert.equal(drop.seen.conns, 3, 'exactly 3 attempts, then it stops');
  await check(c, wId); d = await settled(c, wId); assert.equal(d.state, 'failed'); assert.match(d.detail, /RTMP|closed|handshake/i);
  await check(c, sId); d = await settled(c, sId, 30000); assert.equal(d.state, 'failed'); assert.match(d.detail, /timed out|time|Unable to read|did not properly respond|would block/i);
  await check(c, xId); d = await settled(c, xId); assert.equal(d.state, 'failed'); assert.match(d.detail, /could not connect|no answer from/);   // Windows takes ~1 s to report a closed port, so the 1.2 s test timeout may answer first
  assert.ok(d.detail.includes(String(deadPort)));
  await check(c, fId); d = await settled(c, fId); assert.equal(d.state, 'ok', d.detail); assert.equal(d.attempt, 3, 'recovered on the third try'); assert.equal(flaky.seen.conns, 3);
  // a failure never leaks the key
  assert.ok(!JSON.stringify(await list(c)).includes(KEY));
  for (const f of fs.readdirSync(path.join(c.dir, 'logs'))) assert.ok(!fs.readFileSync(path.join(c.dir, 'logs', f), 'utf8').includes(KEY), 'key in log ' + f);
  assert.match(c.log('stream'), /multistream check failed: drop/); assert.match(c.log('stream'), /multistream check ok: good/);
});

test('Multistream check: removing a destination cancels its running check; the others are unaffected', async () => {
  const silent = await rtmpServer('silent'), good = await rtmpServer('ok');
  const c = await startCore({ env: Object.assign({}, ENV, { IXC_EP_MULTISTREAM_TIMEOUT_MS: '20000', IXC_EP_MULTISTREAM_ATTEMPTS: '5' }) }); cleanup.push(() => c.stop());
  const sId = (await dest(c, { platform: 'custom', name: 'silent', url: silent.url })).body.destination.id; const gId = (await dest(c, { platform: 'custom', name: 'good', url: good.url })).body.destination.id;
  await check(c, sId); await until(async () => (await one(c, sId)).state === 'checking' && silent.seen.conns >= 1, 5000, 'checking');
  await check(c, gId); assert.equal((await settled(c, gId)).state, 'ok');
  assert.equal((await api(c, '/api/multistream/delete', { id: sId })).status, 200); await sleep(800);
  assert.equal(silent.seen.conns, 1, 'no further attempts after it was removed'); assert.equal((await list(c)).destinations.length, 1);
  assert.equal((await check(c, sId)).status, 400);
});
