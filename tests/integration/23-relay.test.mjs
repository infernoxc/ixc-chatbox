// Native multistream relay: a fake OBS publishes to IXC's local RTMP ingest; fake platform ingests (independent RTMP implementation in rtmp.mjs)
// receive the SAME packets. Covers: one and several destinations, byte-identical forwarding, a failing destination that doesn't stop the others,
// reconnection, cancellation / clean shutdown, wrong ingest key, one publisher at a time, stream keys never exposed.
// NOT covered (no way to test offline): real Twitch / YouTube / Kick / Facebook ingest servers, real OBS, a destination so slow that its queue overflows.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os'; import { execFileSync } from 'node:child_process';
import { startCore, api, until, freePort, sleep } from './harness.mjs'; import { fakeDestination, fakePublisher } from './rtmp.mjs'; import net from 'node:net'; import crypto from 'node:crypto';
const require_sha1 = (p) => crypto.createHash('sha1').update(p).digest('hex');

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const KEY = 'live_987654321_TopSecretPlatformKey';
const dests = async (c) => (await api(c, '/api/multistream')).body.destinations;
const one = async (c, id) => (await dests(c)).find(d => d.id === id);
async function boot(extra = {}) {
  const port = await freePort(); const c = await startCore({ env: Object.assign({ IXC_EP_MULTISTREAM_INGEST_PORT: String(port), IXC_EP_MULTISTREAM_RETRY_MS: '150', IXC_EP_MULTISTREAM_BACKOFF_MAX_MS: '600', IXC_EP_MULTISTREAM_TIMEOUT_MS: '3000' }, extra) }); cleanup.push(() => c.stop());
  const r = await api(c, '/api/multistream/relay', { action: 'start' }); assert.equal(r.status, 200, JSON.stringify(r.body)); assert.equal(r.body.relay.running, true);
  return { c, ingestPort: port, token: r.body.relay.streamKey, relay: r.body.relay }; }
async function addDest(c, d, name, key = KEY) { const r = await api(c, '/api/multistream/save', { platform: 'custom', name, url: d.url, key }); assert.equal(r.status, 200, JSON.stringify(r.body)); return r.body.destination.id; }
const liveAll = (c, ids, ms = 10000) => until(async () => { const l = await dests(c); return ids.every(id => l.find(d => d.id === id)?.relay.state === 'live') ? l : null; }, ms, 'destinations live').catch(async e => { throw new Error(e.message + ' ' + JSON.stringify((await dests(c)).map(d => [d.relay.state, d.relay.detail]))); });
// sends a stream: keyframe every 10 video frames; returns what was sent so it can be compared
async function stream(pub, n, t0 = 1000, startIdx = 0) { const sent = []; for (let i = startIdx; i < startIdx + n; i++) { const ts = t0 + i * 33; const v = pub.video(ts, i % 10 === 0, i === 0 ? 20000 : 3000); sent.push({ type: 9, ts, p: v, key: i % 10 === 0 }); const a = pub.audio(ts); sent.push({ type: 8, ts, p: a }); await sleep(3); } return sent; }
const media = (c) => c.msgs.filter(m => (m.type === 8 || m.type === 9)).slice(0);

test('Relay, one destination: OBS stream arrives byte-identical (headers first, then from a keyframe), correct RTMP, key only in the publish command', async () => {
  const d = await fakeDestination({ streamId: 7 }); cleanup.push(d.close);
  const { c, ingestPort, token, relay } = await boot(); assert.equal(relay.serverUrl, 'rtmp://127.0.0.1:' + ingestPort + '/live'); assert.match(token, /^ixc-/);
  const id = await addDest(c, d, 'Fake Twitch');
  const pub = await fakePublisher(ingestPort, token); await pub.ready; const meta = pub.meta(), aseq = pub.audioSeq(), vseq = pub.videoSeq();
  await liveAll(c, [id]); const sent = await stream(pub, 40); pub.video(17000000, true, 500); const lastKey = sent.length;   // a timestamp above 0xFFFFFF uses the extended-timestamp field
  await until(() => d.live()[0] && d.live()[0].msgs.length >= 3 + sent.length + 1, 8000, 'frames arrived');
  const r = d.live()[0]; assert.equal(r.app, 'app'); assert.equal(r.tcUrl, 'rtmp://127.0.0.1:' + d.port + '/app'); assert.match(r.flashVer, /IXC/); assert.equal(r.key, KEY); assert.ok(r.chunkSize === 4096, 'IXC announced its chunk size');
  assert.ok(r.raw.includes('releaseStream') && r.raw.includes('FCPublish'));
  assert.deepEqual(r.msgs.slice(0, 3).map(m => [m.type, m.ts, m.sid]), [[18, 0, 7], [8, 0, 7], [9, 0, 7]], 'description + codec headers first, on the stream id the server gave');
  assert.ok(r.msgs[0].payload.equals(meta) && r.msgs[1].payload.equals(aseq) && r.msgs[2].payload.equals(vseq), 'headers byte-identical');
  const got = r.msgs.slice(3); const base = sent[0].ts;
  for (let i = 0; i < sent.length; i++) { assert.equal(got[i].type, sent[i].type); assert.ok(got[i].payload.equals(sent[i].p), 'packet ' + i + ' is byte-identical (not re-encoded)'); assert.equal(got[i].ts, sent[i].ts - base, 'timestamp ' + i); assert.equal(got[i].sid, 7); }
  assert.equal(got[sent.length].ts, 17000000 - base, 'extended timestamp'); assert.equal(got[sent.length].type, 9);
  const v = await one(c, id); assert.equal(v.relay.state, 'live'); assert.ok(v.relay.frames >= sent.length); assert.equal(v.relay.droppedFrames, 0);
  // the platform key is never shown anywhere
  const all = JSON.stringify((await api(c, '/api/multistream')).body) + JSON.stringify((await api(c, '/api/diag')).body) + JSON.stringify((await api(c, '/api/settings')).body) + JSON.stringify(pub.got);
  assert.ok(!all.includes(KEY)); for (const f of fs.readdirSync(path.join(c.dir, 'logs'))) assert.ok(!fs.readFileSync(path.join(c.dir, 'logs', f), 'utf8').includes(KEY), 'key in log ' + f);
  assert.ok(!fs.readFileSync(path.join(c.dir, 'secrets.dat')).toString('latin1').includes(KEY));
  pub.end();
});

test('Relay, eight destinations: all receive the same stream at once; a destination that rejects the key never stops the others; it keeps retrying with its reason', async () => {
  const ds = []; for (let i = 0; i < 8; i++) ds.push(await fakeDestination({ streamId: 3 + i, reject: i === 5 ? 'NetStream.Publish.BadName' : null })); cleanup.push(async () => { for (const d of ds) await d.close(); });
  const { c, ingestPort, token } = await boot(); const ids = []; for (let i = 0; i < 8; i++) ids.push(await addDest(c, ds[i], 'D' + i, KEY + i));
  assert.equal((await api(c, '/api/multistream/save', { platform: 'custom', url: 'rtmp://127.0.0.1:1/x' })).status, 400, 'a ninth destination is refused');
  const pub = await fakePublisher(ingestPort, token); await pub.ready; pub.meta(); pub.audioSeq(); pub.videoSeq();
  const good = ids.filter((_, i) => i !== 5); await liveAll(c, good); const sent = await stream(pub, 30);
  await until(() => ds.every((d, i) => i === 5 || (d.live()[0] && d.live()[0].msgs.length >= 3 + sent.length)), 10000, 'all seven good destinations got everything');
  const ref = ds[0].live()[0].msgs.map(m => m.hash);
  for (let i = 0; i < 8; i++) { if (i === 5) continue; assert.deepEqual(ds[i].live()[0].msgs.map(m => m.hash), ref, 'destination ' + i + ' got the identical stream'); assert.equal(ds[i].live()[0].key, KEY + i, 'its own key'); }
  // the rejecting destination: never live, reason shown, keeps retrying, key never in the reason
  await until(async () => { const v = await one(c, ids[5]); return v.relay.state === 'reconnecting' && v.relay.reconnects >= 2 ? v : null; }, 8000, 'retrying');
  const bad = await one(c, ids[5]); assert.match(bad.relay.detail, /NetStream\.Publish\.BadName/); assert.ok(!JSON.stringify(bad).includes(KEY)); assert.ok(ds[5].info.conns >= 3, 'it tried again');
  assert.equal(ds[5].info.all.every(x => x.msgs.length === 0), true, 'no media was ever sent to the rejecting server');
  for (const i of [0, 3, 7]) assert.equal((await one(c, ids[i])).relay.state, 'live');
  pub.end();
});

test('Relay: a destination whose connection drops reconnects, gets the headers again and rejoins at a keyframe; the others are unaffected', async () => {
  const a = await fakeDestination({}), b = await fakeDestination({}); cleanup.push(a.close, b.close);
  const { c, ingestPort, token } = await boot(); const ia = await addDest(c, a, 'A'), ib = await addDest(c, b, 'B');
  const pub = await fakePublisher(ingestPort, token); await pub.ready; const meta = pub.meta(), aseq = pub.audioSeq(), vseq = pub.videoSeq(); await liveAll(c, [ia, ib]);
  const sent1 = await stream(pub, 20); await until(() => a.live()[0]?.msgs.length >= 3 + sent1.length, 5000, 'first half arrived');
  a.dropAll();   // the platform hangs up on A only
  await until(async () => { const v = await one(c, ia); return v.relay.state !== 'live' ? v : null; }, 5000, 'A noticed');
  const sent2 = []; for (let r = 0; r < 60 && !(a.live().length && a.live()[0].msgs.length > 3); r++) { sent2.push(...await stream(pub, 5, 100000, 20 + sent2.length / 2)); }
  await until(() => a.live()[0]?.msgs.length > 6, 8000, 'A is back');
  const r2 = a.live()[0]; assert.equal(a.info.conns, 2, 'exactly one new connection'); assert.equal(r2.key, KEY);
  assert.ok(r2.msgs[0].payload.equals(meta) && r2.msgs[1].payload.equals(aseq) && r2.msgs[2].payload.equals(vseq), 'headers sent again');
  const first = r2.msgs[3]; assert.equal(first.type, 9); assert.equal(first.ts, 0, 'timestamps start from the keyframe it joined at'); assert.equal(first.payload[0], 0x17, 'it joined at a keyframe');
  assert.ok((await one(c, ia)).relay.reconnects >= 1);
  // B never noticed
  assert.equal(b.info.conns, 1); const bm = b.live()[0].msgs; assert.deepEqual(bm.slice(3, 3 + sent1.length).map(m => m.hash), sent1.map(s => require_sha1(s.p)));
  pub.end();
});

test('Relay: clean shutdown and cancellation - OBS stops, a destination is deleted, the relay is stopped; wrong key and a second publisher are refused', async () => {
  const a = await fakeDestination({}), b = await fakeDestination({}); cleanup.push(a.close, b.close);
  const { c, ingestPort, token } = await boot(); const ia = await addDest(c, a, 'A'), ib = await addDest(c, b, 'B');
  // wrong ingest key: refused, nothing starts
  const bad = await fakePublisher(ingestPort, 'not-the-key'); await assert.rejects(bad.ready, /BadName/); await sleep(300); assert.equal(a.info.conns, 0); assert.equal((await api(c, '/api/multistream/relay')).body.relay.publishing, false);
  const pub = await fakePublisher(ingestPort, token); await pub.ready; pub.meta(); pub.audioSeq(); pub.videoSeq(); await liveAll(c, [ia, ib]);
  // a second publisher while OBS is live is turned away
  const second = net_connect(ingestPort); await until(() => second.closed, 3000, 'second publisher closed');
  await stream(pub, 15); await until(() => a.live()[0]?.msgs.length > 20 && b.live()[0]?.msgs.length > 20, 5000, 'both receiving');
  // deleting a destination mid-stream stops only that one, politely
  assert.equal((await api(c, '/api/multistream/delete', { id: ia })).status, 200); await until(() => a.info.all[0].closed, 5000, 'A closed'); assert.ok(a.info.all[0].unpublished, 'A was told the stream ended');
  const nb = b.live()[0].msgs.length; await stream(pub, 10, 5000, 15); await until(() => b.live()[0].msgs.length > nb + 10, 5000, 'B continues'); assert.equal(a.info.conns, 1, 'A is not contacted again');
  // OBS stops: B is told and hangs up, state back to idle, no reconnect attempts
  pub.end(); await until(() => b.info.all[0].closed, 5000, 'B closed'); assert.ok(b.info.all[0].unpublished);
  await until(async () => (await one(c, ib)).relay.state === 'idle', 5000, 'idle'); await sleep(700); assert.equal(b.info.conns, 1, 'no reconnect after OBS stopped');
  // a new OBS session starts it again; stopping the relay closes everything and frees the port
  const pub2 = await fakePublisher(ingestPort, token); await pub2.ready; pub2.meta(); pub2.audioSeq(); pub2.videoSeq(); await until(() => b.info.conns === 2 && b.live().length === 1, 8000, 'B again');
  assert.equal((await api(c, '/api/multistream/relay', { action: 'stop' })).body.relay.running, false); await until(() => b.info.all[1].closed, 5000, 'B closed by stop'); await until(() => pub2.got.closed, 5000, 'OBS side closed');
  await assert.rejects(fakePublisher(ingestPort, token).then(p => p.ready), 'the port is closed');
  assert.equal((await api(c, '/api/multistream/relay', { action: 'nope' })).status, 400);
});
function net_connect(port) { const s = net.connect(port, '127.0.0.1'); const o = { closed: false }; s.on('close', () => { o.closed = true; }); s.on('error', () => {}); return o; }

test('Relay: the ingest port already in use is reported; a destination without a key or switched off is skipped with the reason', async () => {
  const hold = net.createServer(); await new Promise(r => hold.listen(0, '127.0.0.1', r)); cleanup.push(() => new Promise(r => hold.close(r)));
  const c0 = await startCore({ env: { IXC_EP_MULTISTREAM_INGEST_PORT: String(hold.address().port) } }); cleanup.push(() => c0.stop());
  const r = await api(c0, '/api/multistream/relay', { action: 'start' }); assert.equal(r.status, 400); assert.match(r.body.error, /another program/);
  const a = await fakeDestination({}); cleanup.push(a.close); const { c, ingestPort, token } = await boot();
  const noKey = (await api(c, '/api/multistream/save', { platform: 'custom', name: 'nokey', url: a.url })).body.destination.id; const off = (await api(c, '/api/multistream/save', { platform: 'custom', name: 'off', url: a.url, key: KEY, enabled: false })).body.destination.id;
  const pub = await fakePublisher(ingestPort, token); await pub.ready; pub.meta(); pub.audioSeq(); pub.videoSeq(); await sleep(500);
  assert.deepEqual([(await one(c, noKey)).relay.state, (await one(c, noKey)).relay.detail], ['skipped', 'no stream key saved']); assert.deepEqual([(await one(c, off)).relay.state, (await one(c, off)).relay.detail], ['skipped', 'turned off']); assert.equal(a.info.conns, 0);
  pub.end();
});

// rtmps:// - needs openssl to make a throw-away certificate; skipped when it is not installed
const opensslOk = (() => { try { execFileSync('openssl', ['version'], { stdio: 'pipe' }); return true; } catch { return false; } })();
test('Relay over rtmps:// (TLS) to a fake destination with a throw-away certificate', { skip: !opensslOk && 'openssl is not installed' }, async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-tls-')); execFileSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', path.join(dir, 'k.pem'), '-out', path.join(dir, 'c.pem'), '-days', '2', '-subj', '/CN=127.0.0.1'], { stdio: 'pipe' });
  const d = await fakeDestination({ tls: { key: fs.readFileSync(path.join(dir, 'k.pem')), cert: fs.readFileSync(path.join(dir, 'c.pem')) } }); cleanup.push(d.close);
  const { c, ingestPort, token } = await boot({ IXC_EP_MULTISTREAM_INSECURE_TLS: '1' }); const id = await addDest(c, d, 'TLS'); assert.match(d.url, /^rtmps:/);
  const pub = await fakePublisher(ingestPort, token); await pub.ready; pub.meta(); pub.audioSeq(); pub.videoSeq(); await liveAll(c, [id]); const sent = await stream(pub, 12);
  await until(() => d.live()[0]?.msgs.length >= 3 + sent.length, 8000, 'frames over TLS'); assert.ok(d.live()[0].msgs.slice(3).every((m, i) => m.payload.equals(sent[i].p))); pub.end();
});
