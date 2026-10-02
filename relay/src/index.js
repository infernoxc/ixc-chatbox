// IXC phone relay (Cloudflare Worker + Durable Object).
//
//   phone --WSS--> /phone/<pcId> --+
//                                  +--> Durable Object "PcHub" (one per PC) <--WSS-- /pc/<pcId>  (IXC on the PC dials out)
//   phone page:   GET /p/<pcId>    (the remote control, served from here so its address never changes)
//   sign-in help: POST /oauth/{youtube|kick}/token   (adds the app secret, so no secret ships inside IXC)
//
// The relay only forwards messages. The PC checks every phone's key (pairing + device keys live on the PC).
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
import PHONE_HTML from '../public/phone.html';

const ID = /^[A-Za-z0-9_-]{16,64}$/;
const MAX_PHONES = 12, MAX_MSG = 65536;
const SEC = {
  'Content-Security-Policy': "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src https: data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
  'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer', 'X-Frame-Options': 'DENY', 'Cache-Control': 'no-store',
  'Strict-Transport-Security': 'max-age=31536000',
};

export default {
  async fetch(req, env) {
    const url = new URL(req.url); const parts = url.pathname.split('/').filter(Boolean);
    if (url.pathname === '/' || url.pathname === '/health') return new Response('IXC relay ok', { headers: { 'Content-Type': 'text/plain', ...SEC } });
    if (parts[0] === 'p' && parts.length === 2 && ID.test(parts[1]) && req.method === 'GET') return new Response(PHONE_HTML, { headers: { 'Content-Type': 'text/html; charset=utf-8', ...SEC, 'Content-Security-Policy': SEC['Content-Security-Policy'].replace("connect-src 'self'", "connect-src 'self' wss://" + url.host + (url.protocol === 'http:' ? ' ws://' + url.host : '')) } });
    if ((parts[0] === 'pc' || parts[0] === 'phone') && parts.length === 2 && ID.test(parts[1])) {
      if ((req.headers.get('Upgrade') || '').toLowerCase() !== 'websocket') return new Response('websocket only', { status: 426, headers: SEC });
      // phones must come from the phone page itself (another website can't open a connection to a PC)
      if (parts[0] === 'phone') { const o = req.headers.get('Origin'); if (o && o !== url.origin) return new Response('origin not allowed', { status: 403, headers: SEC }); }
      return env.HUB.get(env.HUB.idFromName(parts[1])).fetch(req);
    }
    if (parts[0] === 'oauth' && parts[2] === 'token' && req.method === 'POST' && (parts[1] === 'youtube' || parts[1] === 'kick')) return broker(parts[1], req, env);
    return new Response('not found', { status: 404, headers: SEC });
  },
};

// ---------- sign-in helper: exchanges / refreshes codes with the app secret ----------
async function broker(p, req, env) {
  let b; try { b = await req.json(); } catch { return json(400, { error: 'bad request' }); }
  const id = p === 'youtube' ? env.GOOGLE_CLIENT_ID : env.KICK_CLIENT_ID, secret = p === 'youtube' ? env.GOOGLE_CLIENT_SECRET : env.KICK_CLIENT_SECRET;
  if (!id || !secret) return json(503, { error: 'sign-in for ' + p + ' is not set up on this relay' });
  const form = new URLSearchParams({ client_id: id, client_secret: secret });
  if (b.grant_type === 'authorization_code') {
    // only IXC's own local address may receive codes
    if (!/^http:\/\/(127\.0\.0\.1|localhost):\d{2,5}\/oauth\/(youtube|kick)$/.test(String(b.redirect_uri || ''))) return json(400, { error: 'bad redirect' });
    for (const k of ['code', 'code_verifier', 'redirect_uri']) { if (typeof b[k] !== 'string' || !b[k] || b[k].length > 2048) return json(400, { error: 'missing ' + k }); form.set(k, b[k]); }
    form.set('grant_type', 'authorization_code');
  } else if (b.grant_type === 'refresh_token') {
    if (typeof b.refresh_token !== 'string' || !b.refresh_token || b.refresh_token.length > 4096) return json(400, { error: 'missing refresh_token' });
    form.set('grant_type', 'refresh_token'); form.set('refresh_token', b.refresh_token);
  } else return json(400, { error: 'bad grant_type' });
  const url = p === 'youtube' ? 'https://oauth2.googleapis.com/token' : 'https://id.kick.com/oauth/token';
  const r = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' }, body: form });
  return new Response(await r.text(), { status: r.status, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' } });
}
function json(status, o) { return new Response(JSON.stringify(o), { status, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' } }); }
async function sha(s) { const d = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(s)); return btoa(String.fromCharCode(...new Uint8Array(d))); }
function eq(a, b) { if (a.length !== b.length) return false; let d = 0; for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i); return d === 0; }

// ---------- one Durable Object per PC ----------
export class PcHub {
  constructor(state, env) {
    this.state = state; this.env = env; this.rate = new Map();
    // keep-alives answered without waking this object (cheaper, works while hibernating)
    state.setWebSocketAutoResponse(new WebSocketRequestResponsePair('{"t":"ping"}', '{"t":"pong"}'));
  }
  pc() { return this.state.getWebSockets('pc').find(ws => { const a = ws.deserializeAttachment(); return a && a.authed; }); }
  phones() { return this.state.getWebSockets('phone'); }
  async fetch(req) {
    const url = new URL(req.url); const role = url.pathname.split('/')[1];
    const pair = new WebSocketPair(); const [client, server] = Object.values(pair);
    const ip = req.headers.get('CF-Connecting-IP') || '', ua = (req.headers.get('User-Agent') || '').slice(0, 160);
    if (role === 'pc') {
      this.state.acceptWebSocket(server, ['pc']); server.serializeAttachment({ authed: false, at: Date.now() });
    } else {
      if (this.phones().length >= MAX_PHONES) { this.state.acceptWebSocket(server, ['dead']); server.send(JSON.stringify({ relay: 'full' })); server.close(1013, 'too many phones'); return new Response(null, { status: 101, webSocket: client }); }
      const c = crypto.randomUUID().replace(/-/g, '').slice(0, 16);
      this.state.acceptWebSocket(server, ['phone', 'c:' + c]); server.serializeAttachment({ c, ip, ua });
      const pc = this.pc();
      if (pc) { pc.send(JSON.stringify({ t: 'open', c, ip, ua })); server.send(JSON.stringify({ relay: 'pc-online' })); }
      else server.send(JSON.stringify({ relay: 'pc-offline' }));
    }
    return new Response(null, { status: 101, webSocket: client });
  }
  async webSocketMessage(ws, msg) {
    const tags = this.state.getTags(ws);
    if (typeof msg !== 'string' || msg.length > MAX_MSG) { try { ws.close(1009, 'message too big'); } catch {} return; }
    if (tags.includes('pc')) return this.fromPc(ws, msg);
    if (tags.includes('phone')) {
      const a = ws.deserializeAttachment(); const now = Date.now(); const r = this.rate.get(a.c) || { t: now, n: 0 };
      if (now - r.t > 10000) { r.t = now; r.n = 0; } if (++r.n > 120) { this.rate.set(a.c, r); return; } this.rate.set(a.c, r);
      const pc = this.pc(); if (!pc) { ws.send(JSON.stringify({ relay: 'pc-offline' })); return; }
      pc.send(JSON.stringify({ t: 'msg', c: a.c, d: msg }));
    }
  }
  async fromPc(ws, msg) {
    let m; try { m = JSON.parse(msg); } catch { return; }
    const a = ws.deserializeAttachment() || {};
    if (!a.authed) {
      if (m.t !== 'hello' || typeof m.secret !== 'string' || m.secret.length < 20) { ws.send(JSON.stringify({ t: 'error', error: 'hello first' })); ws.close(1008, 'hello first'); return; }
      const h = await sha(m.secret); const known = await this.state.storage.get('secret');
      if (known && !eq(known, h)) { ws.send(JSON.stringify({ t: 'error', error: 'bad-secret' })); ws.close(1008, 'bad secret'); return; }
      if (!known) await this.state.storage.put('secret', h);   // first PC to use this (random, unguessable) id owns it
      ws.serializeAttachment({ authed: true, at: Date.now(), v: String(m.v || '').slice(0, 20) });
      for (const old of this.state.getWebSockets('pc')) if (old !== ws) { try { old.close(1012, 'replaced'); } catch {} }   // a newer connection from the same PC wins
      ws.send(JSON.stringify({ t: 'ready' }));
      for (const p of this.phones()) { const pa = p.deserializeAttachment(); ws.send(JSON.stringify({ t: 'open', c: pa.c, ip: pa.ip, ua: pa.ua })); try { p.send(JSON.stringify({ relay: 'pc-online' })); } catch {} }
      return;
    }
    if (m.t === 'msg' && typeof m.c === 'string' && typeof m.d === 'string') { const p = this.state.getWebSockets('c:' + m.c)[0]; if (p) { try { p.send(m.d); } catch {} } return; }
    if (m.t === 'close' && typeof m.c === 'string') { const p = this.state.getWebSockets('c:' + m.c)[0]; if (p) { try { p.close(1000, String(m.reason || '').slice(0, 100)); } catch {} } return; }
  }
  async webSocketClose(ws) { this.gone(ws); }
  async webSocketError(ws) { this.gone(ws); }
  gone(ws) {
    const tags = this.state.getTags(ws);
    if (tags.includes('phone')) { const a = ws.deserializeAttachment(); this.rate.delete(a.c); const pc = this.pc(); if (pc) { try { pc.send(JSON.stringify({ t: 'close', c: a.c })); } catch {} } }
    else if (tags.includes('pc')) {
      const a = ws.deserializeAttachment() || {}; if (!a.authed) return;
      if (this.state.getWebSockets('pc').some(x => x !== ws && (x.deserializeAttachment() || {}).authed)) return;
      for (const p of this.phones()) { try { p.send(JSON.stringify({ relay: 'pc-offline' })); } catch {} }
    }
  }
}
