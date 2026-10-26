// Test-only RTMP: an independent implementation (written separately from the C# one) of a minimal RTMP server (a fake platform ingest)
// and a minimal RTMP publisher (a fake OBS). Used to check that IXC's relay forwards the exact bytes and speaks correct RTMP.
import net from 'node:net'; import tls from 'node:tls'; import crypto from 'node:crypto';

// ----- AMF0 -----
export function amfEnc(items) {
  const out = []; const num = (n) => { const b = Buffer.alloc(9); b[0] = 0; b.writeDoubleBE(n, 1); return b; }; const str = (s) => { const t = Buffer.from(s); const h = Buffer.alloc(2); h.writeUInt16BE(t.length); return Buffer.concat([h, t]); };
  const val = (v) => { if (v === null) return Buffer.from([5]); if (typeof v === 'string') return Buffer.concat([Buffer.from([2]), str(v)]); if (typeof v === 'number') return num(v); if (typeof v === 'boolean') return Buffer.from([1, v ? 1 : 0]);
    const parts = [Buffer.from([3])]; for (const [k, x] of Object.entries(v)) parts.push(str(k), val(x)); parts.push(Buffer.from([0, 0, 9])); return Buffer.concat(parts); };
  for (const i of items) out.push(val(i)); return Buffer.concat(out); }
export function amfDec(buf) {
  let i = 0; const rd = () => { const t = buf[i++]; if (t === 0) { const v = buf.readDoubleBE(i); i += 8; return v; } if (t === 1) return buf[i++] !== 0; if (t === 2) { const n = buf.readUInt16BE(i); i += 2; const s = buf.toString('utf8', i, i + n); i += n; return s; }
    if (t === 3 || t === 8) { if (t === 8) i += 4; const o = {}; for (;;) { const n = buf.readUInt16BE(i); i += 2; if (n === 0 && buf[i] === 9) { i++; return o; } const k = buf.toString('utf8', i, i + n); i += n; o[k] = rd(); } } if (t === 5 || t === 6) return null; throw new Error('amf ' + t); };
  const out = []; try { while (i < buf.length) out.push(rd()); } catch {} return out; }

// ----- chunk stream -----
export class ChunkParser {
  constructor(onMsg) { this.buf = Buffer.alloc(0); this.size = 128; this.cs = new Map(); this.onMsg = onMsg; }
  push(d) { this.buf = Buffer.concat([this.buf, d]); for (;;) { if (!this.one()) break; } }
  one() {
    const b = this.buf; if (b.length < 1) return false; let o = 0; const fmt = b[0] >> 6; let id = b[0] & 63; o = 1;
    if (id === 0) { if (b.length < 2) return false; id = b[1] + 64; o = 2; } else if (id === 1) { if (b.length < 3) return false; id = b[1] + b[2] * 256 + 64; o = 3; }
    let st = this.cs.get(id); if (!st) { st = { ts: 0, delta: 0, len: 0, type: 0, sid: 0, buf: null, ext: false }; this.cs.set(id, st); }
    const hl = [11, 7, 3, 0][fmt]; if (b.length < o + hl) return false; const h = b.subarray(o, o + hl); o += hl;
    let ts = fmt < 3 ? (h[0] << 16 | h[1] << 8 | h[2]) : 0; let len = st.len, type = st.type, sid = st.sid; if (fmt <= 1) { len = h[3] << 16 | h[4] << 8 | h[5]; type = h[6]; } if (fmt === 0) sid = h.readUInt32LE(7);
    let ext = fmt < 3 ? ts === 0xFFFFFF : st.ext; if (ext) { if (b.length < o + 4) return false; ts = b.readUInt32BE(o); o += 4; }
    const fresh = st.buf === null; const need = fresh ? len : len - st.buf.length; const take = Math.min(need, this.size); if (b.length < o + take) return false;
    // commit
    st.ext = ext; st.len = len; st.type = type; st.sid = sid; if (fmt === 0) { st.ts = ts; st.delta = 0; } else if (fmt < 3) { st.delta = ts; if (fresh) st.ts = (st.ts + ts) >>> 0; } else if (fresh) st.ts = (st.ts + st.delta) >>> 0;
    if (fresh) st.buf = Buffer.alloc(0); st.buf = Buffer.concat([st.buf, b.subarray(o, o + take)]); this.buf = b.subarray(o + take);
    if (st.buf.length === len) { const m = { type, ts: st.ts, sid, payload: st.buf, csid: id }; st.buf = null; if (type === 1) this.size = m.payload.readUInt32BE(0) & 0x7FFFFFFF; this.onMsg(m); }
    return true; }
}
export class ChunkWriter {
  constructor(sock) { this.sock = sock; this.size = 128; }
  send(type, ts, sid, payload, csid) {
    csid = csid || (type <= 6 ? 2 : type === 20 ? 3 : type === 8 ? 4 : 5); const ext = ts >= 0xFFFFFF; const hd = Buffer.alloc(12); hd[0] = csid; hd.writeUIntBE(ext ? 0xFFFFFF : ts, 1, 3); hd.writeUIntBE(payload.length, 4, 3); hd[7] = type; hd.writeUInt32LE(sid, 8);
    const parts = [hd]; const e = Buffer.alloc(4); e.writeUInt32BE(ts >>> 0); if (ext) parts.push(e); let off = 0;
    for (;;) { const n = Math.min(this.size, payload.length - off); parts.push(payload.subarray(off, off + n)); off += n; if (off >= payload.length) break; parts.push(Buffer.from([0xC0 | csid])); if (ext) parts.push(e); }
    this.sock.write(Buffer.concat(parts)); }
  setChunk(n) { const b = Buffer.alloc(4); b.writeUInt32BE(n); this.send(1, 0, 0, b); this.size = n; }
}

// ----- a fake platform ingest -----
// opts: { reject: 'NetStream.Publish.BadName' | null, streamId: 7, tls: {key, cert} | null, rejectNth, port (default: any free) }
export async function fakeDestination(opts = {}) {
  const info = { conns: 0, live: [], all: [] }; const socks = new Set();
  const handler = (sock) => { socks.add(sock); sock.on('close', () => socks.delete(sock)); sock.on('error', () => {}); const n = ++info.conns;
    const c = { n, sock, app: null, tcUrl: null, flashVer: null, key: null, msgs: [], meta: null, closed: false, published: false, unpublished: false, chunkSize: null, raw: [] }; info.all.push(c); sock.on('close', () => { c.closed = true; });
    const w = new ChunkWriter(sock); w.size = 128; let hs = 0; let buf = Buffer.alloc(0);
    const parser = new ChunkParser((m) => {
      if (m.type === 1) c.chunkSize = m.payload.readUInt32BE(0);
      if (m.type === 20) { const a = amfDec(m.payload); const name = a[0], txn = a[1];
        if (name === 'connect') { c.app = a[2].app; c.tcUrl = a[2].tcUrl; c.flashVer = a[2].flashVer; w.send(5, 0, 0, Buffer.from([0, 0x26, 0x25, 0xA0])); w.send(6, 0, 0, Buffer.from([0, 0x26, 0x25, 0xA0, 2])); w.setChunk(4096);
          w.send(20, 0, 0, amfEnc(['_result', txn, { fmsVer: 'FMS/3,0,1,123' }, { level: 'status', code: 'NetConnection.Connect.Success', description: 'ok' }])); }
        else if (name === 'releaseStream' || name === 'FCPublish') { c.raw.push(name); w.send(20, 0, 0, amfEnc(['_result', txn, null, null])); }
        else if (name === 'createStream') w.send(20, 0, 0, amfEnc(['_result', txn, null, opts.streamId || 7]));
        else if (name === 'publish') { c.key = a[3]; c.publishSid = m.sid; const bad = typeof opts.reject === 'function' ? opts.reject(n) : opts.reject;
          if (bad) { w.send(20, 0, m.sid, amfEnc(['onStatus', 0, null, { level: 'error', code: bad, description: 'rejected ' + (c.key || '') }])); return; }
          c.published = true; w.send(20, 0, m.sid, amfEnc(['onStatus', 0, null, { level: 'status', code: 'NetStream.Publish.Start', description: 'start' }])); }
        else if (name === 'FCUnpublish' || name === 'deleteStream') c.unpublished = true; }
      else if (m.type === 8 || m.type === 9 || m.type === 18) { c.msgs.push({ type: m.type, ts: m.ts, sid: m.sid, payload: m.payload, hash: crypto.createHash('sha1').update(m.payload).digest('hex') }); if (opts.onMsg) opts.onMsg(c, m); } });
    sock.on('data', (d) => { if (hs < 2) { buf = Buffer.concat([buf, d]);
        if (hs === 0 && buf.length >= 1537) { const s1 = crypto.randomBytes(1536); s1.writeUInt32BE(0, 0); s1.writeUInt32BE(0, 4); sock.write(Buffer.concat([Buffer.from([3]), s1, buf.subarray(1, 1537)])); buf = buf.subarray(1537); hs = 1; }
        if (hs === 1 && buf.length >= 1536) { buf = buf.subarray(1536); hs = 2; if (buf.length) parser.push(buf); buf = Buffer.alloc(0); } }
      else parser.push(d); }); };
  const srv = opts.tls ? tls.createServer({ key: opts.tls.key, cert: opts.tls.cert }, handler) : net.createServer(handler);
  await new Promise(r => srv.listen(opts.port || 0, '127.0.0.1', r)); const port = srv.address().port;
  return { port, info, url: (opts.tls ? 'rtmps' : 'rtmp') + '://127.0.0.1:' + port + '/app', live: () => info.all.filter(c => c.published && !c.closed), dropAll: () => { for (const s of socks) s.destroy(); },
    close: () => new Promise(r => { for (const s of socks) s.destroy(); srv.close(r); }) }; }

// ----- a fake OBS -----
export async function fakePublisher(port, key, opts = {}) {
  const sock = net.connect(port, '127.0.0.1'); sock.on('error', () => {}); const got = { status: [], results: [] }; let hs = 0; let buf = Buffer.alloc(0); const w = new ChunkWriter(sock); let sid = 0; let resolveReady, rejectReady; const ready = new Promise((a, b) => { resolveReady = a; rejectReady = b; });
  const parser = new ChunkParser((m) => { if (m.type === 20) { const a = amfDec(m.payload); got.results.push(a);
    if (a[0] === '_result' && a[1] === 1) { w.send(20, 0, 0, amfEnc(['releaseStream', 2, null, key])); w.send(20, 0, 0, amfEnc(['FCPublish', 3, null, key])); w.send(20, 0, 0, amfEnc(['createStream', 4, null])); }
    else if (a[0] === '_result' && a[1] === 4) { sid = a[3]; w.send(20, 0, sid, amfEnc(['publish', 5, null, key, 'live'])); }
    else if (a[0] === 'onStatus') { got.status.push(a[3].code); if (a[3].code === 'NetStream.Publish.Start') resolveReady(); else rejectReady(new Error(a[3].code)); } } });
  sock.on('close', () => { got.closed = true; rejectReady(new Error('closed')); }); ready.catch(() => {});
  sock.on('connect', () => { const c1 = crypto.randomBytes(1537); c1[0] = 3; c1.fill(0, 1, 9); sock.write(c1); });
  sock.on('data', (d) => { if (hs < 2) { buf = Buffer.concat([buf, d]); if (hs === 0 && buf.length >= 1 + 1536 + 1536) { const s1 = buf.subarray(1, 1537); sock.write(s1); buf = buf.subarray(1 + 1536 + 1536); hs = 2;
        w.setChunk(opts.chunk || 4096); w.send(20, 0, 0, amfEnc(['connect', 1, { app: 'live', type: 'nonprivate', tcUrl: 'rtmp://127.0.0.1:' + port + '/live' }])); if (buf.length) parser.push(buf); buf = Buffer.alloc(0); } } else parser.push(d); });
  const api = { sock, got, ready, w, sid: () => sid,
    meta() { const p = amfEnc(['@setDataFrame', 'onMetaData', { width: 1920, height: 1080, videocodecid: 7, audiocodecid: 10 }]); w.send(18, 0, sid, p); return p; },
    audioSeq() { const p = Buffer.from([0xAF, 0x00, 0x12, 0x10]); w.send(8, 0, sid, p); return p; },
    videoSeq() { const p = Buffer.concat([Buffer.from([0x17, 0x00, 0, 0, 0]), crypto.randomBytes(40)]); w.send(9, 0, sid, p); return p; },
    audio(ts, n = 200) { const p = Buffer.concat([Buffer.from([0xAF, 0x01]), crypto.randomBytes(n)]); w.send(8, ts, sid, p); return p; },
    video(ts, key, n = 3000) { const p = Buffer.concat([Buffer.from([key ? 0x17 : 0x27, 0x01, 0, 0, 0]), crypto.randomBytes(n)]); w.send(9, ts, sid, p); return p; },
    end() { try { w.send(20, 0, sid, amfEnc(['deleteStream', 6, null, sid])); } catch {} sock.end(); }, kill() { sock.destroy(); } };
  return api; }
