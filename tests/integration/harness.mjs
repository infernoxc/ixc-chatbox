// Test harness: builds IXC Core with Mono, runs it in --test mode in a temp folder, and provides fake platform servers.
// Part of IXC - (c) 2026 Ishan (InFerNoxC) - MIT License
import { spawn, execFileSync } from 'node:child_process';
import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os'; import http from 'node:http'; import net from 'node:net';
import { fileURLToPath } from 'node:url';
import { WebSocketServer, WebSocket } from 'ws';

const here = path.dirname(fileURLToPath(import.meta.url)); export const repo = path.resolve(here, '../..');
let built = null;
export const sleep = (ms) => new Promise(r => setTimeout(r, ms));
export async function freePort() { return new Promise((res) => { const s = net.createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); }); }
export function build() {
  if (built) return built; const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-build-')); const app = path.join(root, 'app');
  execFileSync('sh', [path.join(repo, 'tests/build-linux.sh'), path.join(app, 'core/ixc-core.exe')], { stdio: 'pipe' });
  for (const d of ['chat', 'music', 'app', 'overlay']) fs.cpSync(path.join(repo, 'src', d), path.join(app, d), { recursive: true });
  fs.cpSync(path.join(repo, 'src/core/web'), path.join(app, 'core/web'), { recursive: true });
  fs.writeFileSync(path.join(app, 'core/VERSION'), '3.0.0');
  built = app; return app; }

// ---------- IXC Core ----------
export async function startCore(opts = {}) {
  const app = build(); const dir = opts.dir || fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-data-')); const port = opts.port || await freePort();
  const cfg = Object.assign({ helper: { port }, obs: { websocketUrl: 'ws://127.0.0.1:1/' }, platforms: { streamerbot: { mode: 'off' } }, general: { firstRunDone: true, checkUpdates: false } }, opts.config || {});
  if (!opts.keepConfig) fs.writeFileSync(path.join(dir, 'config.json'), opts.rawConfig ?? JSON.stringify(cfg, null, 2));
  if (opts.defaults) fs.writeFileSync(path.join(app, 'core/ixc.defaults.json'), JSON.stringify(opts.defaults)); else if (fs.existsSync(path.join(app, 'core/ixc.defaults.json'))) fs.rmSync(path.join(app, 'core/ixc.defaults.json'));
  const env = Object.assign({}, process.env, { IXC_TTS_FAKE: '1', IXC_OBS_CONFIG: path.join(dir, 'obs-config'), IXC_EP_NET_CHECK: 'http://127.0.0.1:1/', IXC_EP_TWITCH_GQL: 'http://127.0.0.1:1/', IXC_EP_YOUTUBE_END_CONFIRM_MS: '3000' }, opts.env || {});
  const proc = spawn('mono', [path.join(app, 'core/ixc-core.exe'), '--test', '--config', path.join(dir, 'config.json')], { env, stdio: ['ignore', 'pipe', 'pipe'] });
  let out = ''; proc.stdout.on('data', d => out += d); proc.stderr.on('data', d => out += d);
  const core = { port, dir, proc, url: 'http://127.0.0.1:' + port, exited: null, output: () => out };
  proc.on('exit', (c) => { core.exited = c; });
  for (let i = 0; i < 100; i++) { await sleep(100); try { const r = await fetch(core.url + '/api/ping'); if (r.ok) break; } catch {} if (core.exited != null) throw new Error('core exited ' + core.exited + '\n' + out); }
  core.stop = async () => { if (core.exited == null) { proc.kill('SIGKILL'); for (let i = 0; i < 50 && core.exited == null; i++) await sleep(50); } };
  core.restart = async (o2 = {}) => { await core.stop(); return startCore(Object.assign({}, opts, o2, { dir, port, keepConfig: true })); };
  core.log = (area) => { try { return fs.readFileSync(path.join(dir, 'logs', (area || 'app') + '.log'), 'utf8'); } catch { return ''; } };
  return core; }
export async function api(core, p, body, headers = {}) {
  const r = await fetch(core.url + p, body === undefined ? { headers } : { method: 'POST', headers: Object.assign({ 'Content-Type': 'application/json' }, headers), body: typeof body === 'string' ? body : JSON.stringify(body) });
  const t = await r.text(); let j; try { j = JSON.parse(t); } catch { j = t; } return { status: r.status, body: j }; }
export async function settings(core, patch) { const r = await api(core, '/api/settings', { patch }); if (r.status !== 200) throw new Error('settings: ' + JSON.stringify(r.body)); return r.body.values; }
export async function inject(core, msg) { return (await api(core, '/api/chat/inject', msg)).body; }

// ---------- WebSocket clients (pages, players, phones) ----------
export async function client(url, hello, opts = {}) {
  const ws = new WebSocket(url, opts); const msgs = []; const waiters = [];
  ws.on('message', (d) => { let m; try { m = JSON.parse(d.toString()); } catch { m = d.toString(); } let used = false;
    for (const w of waiters.slice()) if (w.pred(m)) { waiters.splice(waiters.indexOf(w), 1); clearTimeout(w.t); w.res(m); if (!w.keep) used = true; }
    if (!used) msgs.push(m); });
  await new Promise((res, rej) => { ws.once('open', res); ws.once('error', rej); ws.once('unexpected-response', (q, r) => rej(new Error('HTTP ' + r.statusCode))); });
  const c = { ws, msgs,
    send: (o) => ws.send(typeof o === 'string' ? o : JSON.stringify(o)),
    wait: (pred, ms = 5000, label = '') => { const p = typeof pred === 'string' ? (m) => m && m.type === pred : pred; const hit = msgs.find(p); if (hit && !opts.fresh) { msgs.splice(msgs.indexOf(hit), 1); return Promise.resolve(hit); }
      return new Promise((res, rej) => { const w = { pred: p, res, t: setTimeout(() => { waiters.splice(waiters.indexOf(w), 1); rej(new Error('timeout waiting for ' + (label || pred.toString().slice(0, 120)))); }, ms) }; waiters.push(w); }); },
    next: (pred, ms = 5000, label = '') => { const p = typeof pred === 'string' ? (m) => m && m.type === pred : pred; return new Promise((res, rej) => { const w = { pred: p, res, keep: true, t: setTimeout(() => { waiters.splice(waiters.indexOf(w), 1); rej(new Error('timeout waiting for ' + (label || pred.toString().slice(0, 120)))); }, ms) }; waiters.push(w); }); },
    clear: () => { msgs.length = 0; },
    close: () => new Promise(r => { if (ws.readyState === 3) return r(); ws.once('close', r); ws.close(); }) };
  if (hello) c.send(hello); return c; }
export async function page(core, role, topics, opts) { return client('ws://127.0.0.1:' + core.port + '/ws', { type: 'hello', role, topics }, opts); }

// ---------- fake servers ----------
export async function httpServer(handler) {
  const port = await freePort(); const hits = [];
  const srv = http.createServer(async (req, res) => { let body = ''; for await (const ch of req) body += ch; hits.push({ method: req.method, url: req.url, body, headers: req.headers });
    try { await handler(req, res, body); } catch (e) { res.writeHead(500); res.end(String(e)); } });
  await new Promise(r => srv.listen(port, '127.0.0.1', r));
  return { port, url: 'http://127.0.0.1:' + port, hits, close: () => new Promise(r => { srv.closeAllConnections?.(); srv.close(r); }) }; }
export function json(res, code, o, headers = {}) { res.writeHead(code, Object.assign({ 'Content-Type': 'application/json' }, headers)); res.end(typeof o === 'string' ? o : JSON.stringify(o)); }
export async function wsServer(onConn, port) {
  port = port || await freePort(); const wss = new WebSocketServer({ port, host: '127.0.0.1' }); const conns = [];
  wss.on('connection', (ws, req) => { const c = { ws, req, lines: [], send: (s) => ws.send(typeof s === 'string' ? s : JSON.stringify(s)) }; conns.push(c); ws.on('close', () => { c.closed = true; }); onConn(c); });
  await new Promise(r => wss.on('listening', r));
  return { port, url: 'ws://127.0.0.1:' + port, conns, live: () => conns.filter(c => !c.closed), close: () => new Promise(r => { for (const c of conns) try { c.ws.terminate(); } catch {} wss.close(r); }) }; }
// Twitch IRC over WebSocket
export async function fakeTwitch(port) {
  const s = await wsServer((c) => {
    c.ws.on('message', (d) => { for (const line of d.toString().split('\r\n').filter(Boolean)) { c.lines.push(line);
      if (line.startsWith('NICK ')) { c.nick = line.slice(5); c.send(':tmi.twitch.tv 001 ' + c.nick + ' :Welcome, GLHF!'); }
      if (line.startsWith('PASS ')) c.pass = line.slice(5);
      if (line.startsWith('JOIN ')) { c.chan = line.slice(5); c.send(':' + c.nick + '!' + c.nick + '@' + c.nick + '.tmi.twitch.tv JOIN ' + c.chan); c.send('@room-id=1 :tmi.twitch.tv ROOMSTATE ' + c.chan); } } }); }, port);
  s.msg = (tags, login, text, chan = '#streamer') => { for (const c of s.live()) c.send('@' + Object.entries(tags).map(([k, v]) => k + '=' + v).join(';') + ' :' + login + '!' + login + '@' + login + '.tmi.twitch.tv PRIVMSG ' + chan + ' :' + text); };
  s.raw = (line) => { for (const c of s.live()) c.send(line); };
  return s; }
// obs-websocket 5
export async function fakeObs(opts = {}) {
  const st = { scenes: opts.scenes || ['Main', 'BRB'], inputs: {}, items: {}, tracks: {}, current: 'Main', calls: [] }; for (const sc of st.scenes) st.items[sc] = [];
  const s = await wsServer((c) => {
    c.send({ op: 0, d: { obsWebSocketVersion: '5.5.0', rpcVersion: 1, authentication: opts.password ? { challenge: 'ch', salt: 'sa' } : undefined } });
    c.ws.on('message', async (d) => { const m = JSON.parse(d.toString());
      if (m.op === 1) { if (opts.password) { const { createHash } = await import('node:crypto'); const b = (x) => createHash('sha256').update(x).digest('base64'); if (m.d.authentication !== b(b(opts.password + 'sa') + 'ch')) { c.ws.close(4009, 'auth failed'); return; } } c.send({ op: 2, d: { negotiatedRpcVersion: 1 } }); return; }
      if (m.op !== 6) return; const { requestType: t, requestId: id, requestData: q = {} } = m.d; st.calls.push(t); let ok = true, comment = '', data = {};
      const fail = (cm) => { ok = false; comment = cm; };
      switch (t) {
        case 'GetVersion': data = { obsVersion: '31.0.0' }; break;
        case 'GetSceneList': data = { scenes: st.scenes.map(n => ({ sceneName: n })), currentProgramSceneName: st.current }; break;
        case 'GetCurrentProgramScene': data = { currentProgramSceneName: st.current, sceneName: st.current }; break;
        case 'CreateScene': if (st.scenes.includes(q.sceneName)) fail('exists'); else { st.scenes.push(q.sceneName); st.items[q.sceneName] = []; } break;
        case 'GetInputList': data = { inputs: Object.entries(st.inputs).filter(([, v]) => !q.inputKind || v.kind === q.inputKind).map(([k, v]) => ({ inputName: k, inputKind: v.kind })) }; break;
        case 'GetInputSettings': if (!st.inputs[q.inputName]) fail('No source was found by the name of `' + q.inputName + '`.'); else data = { inputSettings: st.inputs[q.inputName].settings, inputKind: st.inputs[q.inputName].kind }; break;
        case 'CreateInput': if (st.inputs[q.inputName]) fail('exists'); else { st.inputs[q.inputName] = { kind: q.inputKind, settings: q.inputSettings || {} }; st.items[q.sceneName].push(q.inputName); st.tracks[q.inputName] = { 1: true, 2: true, 3: true, 4: true, 5: true, 6: true }; } break;
        case 'SetInputSettings': if (!st.inputs[q.inputName]) fail('No source was found'); else Object.assign(st.inputs[q.inputName].settings, q.inputSettings); break;
        case 'GetSceneItemId': if ((st.items[q.sceneName] || []).includes(q.sourceName)) data = { sceneItemId: 1 + st.items[q.sceneName].indexOf(q.sourceName) }; else fail('No scene items were found'); break;
        case 'CreateSceneItem': if (!st.items[q.sceneName]) fail('no scene'); else { st.items[q.sceneName].push(q.sourceName); data = { sceneItemId: st.items[q.sceneName].length }; } break;
        case 'SetInputAudioTracks': if (!st.tracks[q.inputName]) fail('No source was found by the name of `' + q.inputName + '`.'); else { if (opts.notReadyOnce && !st.nr) { st.nr = 1; ok = false; comment = 'OBS is not ready to perform the request.'; break; } Object.assign(st.tracks[q.inputName], q.inputAudioTracks); } break;
        case 'GetInputAudioTracks': if (!st.tracks[q.inputName]) fail('No source was found'); else data = { inputAudioTracks: st.tracks[q.inputName] }; break;
        case 'GetStreamStatus': data = { outputActive: !!st.streaming }; break;
        case 'GetRecordStatus': data = { outputActive: !!st.recording }; break;
        default: fail('unknown request ' + t); }
      c.send({ op: 7, d: { requestType: t, requestId: id, requestStatus: { result: ok, code: ok ? 100 : 600, comment }, responseData: data } }); }); });
  s.st = st; return s; }
// Cloudflare Worker relay in Cloudflare's local runtime (wrangler dev)
export async function startRelay() {
  const port = await freePort(); const bin = path.join(repo, 'tests/node_modules/.bin/wrangler');
  const persist = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-relay-'));
  const proc = spawn(bin, ['dev', '--port', String(port), '--ip', '127.0.0.1', '--persist-to', persist, '--show-interactive-dev-session=false'], { cwd: path.join(repo, 'relay'), env: Object.assign({}, process.env, { WRANGLER_SEND_METRICS: 'false', CI: '1' }), stdio: ['ignore', 'pipe', 'pipe'] });
  let out = ''; proc.stdout.on('data', d => out += d); proc.stderr.on('data', d => out += d);
  for (let i = 0; i < 300; i++) { await sleep(200); try { const r = await fetch('http://127.0.0.1:' + port + '/health'); if (r.ok) break; } catch {} }
  return { port, url: 'http://127.0.0.1:' + port, proc, output: () => out, stop: async () => { proc.kill('SIGTERM'); await sleep(300); try { proc.kill('SIGKILL'); } catch {} } }; }
export async function until(fn, ms = 8000, label = 'condition') { const t0 = Date.now(); let last; while (Date.now() - t0 < ms) { try { last = await fn(); if (last) return last; } catch (e) { last = e; } await sleep(100); } throw new Error('timeout: ' + label + (last instanceof Error ? ' (' + last.message + ')' : '')); }
