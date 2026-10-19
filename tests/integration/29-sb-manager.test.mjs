// Streamer.bot as a managed backend: settings (write-only password, localhost only, persistence), connection test, status, reconnect,
// wrong password, echo of IXC's own replies, duplicate events, and start/stop of a process IXC started (a harmless stand-in program named
// Streamer.bot.exe, Windows only). MOCK: no real Streamer.bot, platform or account is contacted.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { execFileSync } from 'node:child_process';
import { startCore, api, page, wsServer, until, sleep, freePort } from './harness.mjs';
import { createHash } from 'node:crypto';

// Hermetic: IXC searches the user's Desktop/Documents for a Streamer.bot settings file (password) and program. The tests point it at a path that does not exist.
const NOSB = path.join(os.tmpdir(), 'ixc-no-streamerbot', 'settings.json');
const sbCfg = (o = {}) => ({ platforms: { streamerbot: Object.assign({ settingsPath: NOSB }, o) } });
const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const H = (x) => createHash('sha256').update(x).digest('base64');
const PW = 'sb-Secret-Pass-12345';
// a fake Streamer.bot WebSocket server: Hello (+ password challenge), Authenticate, Subscribe, GetBroadcaster, SendMessage (echoes it as a chat event)
async function fakeSb({ password = PW, port, echo = true } = {}) {
  const sb = { sends: [] };
  const srv = await wsServer((c) => {
    c.send(JSON.stringify({ request: 'Hello', info: { name: 'Streamer.bot', version: '1.0.7', instanceId: 'abc' }, authentication: password ? { salt: 's', challenge: 'c' } : undefined }));
    c.ws.on('message', (d) => { const m = JSON.parse(d.toString()); c.lines.push(m);
      const ok = (extra = {}) => c.send(JSON.stringify(Object.assign({ id: m.id, status: 'ok' }, extra)));
      if (m.request === 'Authenticate') { if (m.authentication === H(H(password + 's') + 'c')) { c.authed = true; ok(); } else c.send(JSON.stringify({ id: m.id, status: 'error', error: 'bad password' })); }
      else if (password && !c.authed) c.send(JSON.stringify({ id: m.id, status: 'error', error: 'authentication required' }));
      else if (m.request === 'Subscribe') ok();
      else if (m.request === 'GetBroadcaster') ok({ platforms: { kick: { broadcastUserName: 'mykick' }, twitch: { broadcastUserName: 'mytwitch' } } });
      else if (m.request === 'GetCommands') ok({ commands: [] });
      else if (m.request === 'SendMessage') { sb.sends.push(m); ok(); if (echo) setTimeout(() => sb.chat(m.platform, m.message, 'echo-' + sb.sends.length, 'mykick'), 150); } }); }, port);
  sb.srv = srv; sb.url = srv.url + '/';
  sb.chat = (platform, text, id, user = 'viewer1') => { for (const c of srv.live()) c.send(JSON.stringify({ event: { source: platform === 'kick' ? 'Kick' : 'Twitch', type: 'ChatMessage' }, data: { message: { msgId: id, message: text, displayName: user, username: user }, user: { name: user, role: 1 } } })); };
  sb.dropAll = () => { for (const c of srv.live()) try { c.ws.terminate(); } catch {} };
  cleanup.push(srv.close); return sb; }
const status = async (c) => (await api(c, '/api/streamerbot')).body;
const everything = async (c) => [JSON.stringify((await api(c, '/api/streamerbot')).body), JSON.stringify((await api(c, '/api/settings')).body), JSON.stringify((await api(c, '/api/diag')).body), JSON.stringify((await api(c, '/api/chat/status')).body),
  fs.readFileSync(path.join(c.dir, 'config.json'), 'utf8'), fs.readFileSync(path.join(c.dir, 'secrets.dat')).toString('latin1'), c.output(), ...fs.readdirSync(path.join(c.dir, 'logs')).map(f => fs.readFileSync(path.join(c.dir, 'logs', f), 'utf8'))].join('\n');

test('MOCK settings: localhost only, bad input refused, password write-only and persistent, nothing leaks', async () => {
  let c = await startCore({ env: { IXC_EP_SB_EXE: 'IxcNoSuch.exe' }, config: sbCfg({ mode: 'off' }) }); cleanup.push(() => c.stop());
  let s = await status(c); assert.equal(s.mode, 'off'); assert.equal(s.launch, 'never');
  for (const [body, re] of [[{ url: 'ws://example.com:8080/' }, /only connects to Streamer.bot on this PC/], [{ url: 'http://127.0.0.1:8080/' }, /ws:\/\//], [{ url: '' }, /ws:\/\//], [{ launch: 'sometimes' }, /launch must/], [{ mode: 'maybe' }, /mode must/], [{ exePath: 'C:\\Windows\\notepad.exe' }, /not a Streamer.bot.exe/], [{}, /nothing to save/], [{ password: ' padded ' }, /spaces/]]) {
    const r = await api(c, '/api/streamerbot/config', body); assert.equal(r.status, 400, JSON.stringify(body)); assert.match(r.body.error, re); }
  const r = await api(c, '/api/streamerbot/config', { url: 'ws://localhost:9999/', password: PW, mode: 'on', launch: 'withIxc' }); assert.equal(r.status, 200, JSON.stringify(r.body)); assert.ok(!JSON.stringify(r.body).includes(PW), 'never echoed');
  s = await status(c); assert.equal(s.url, 'ws://localhost:9999/'); assert.equal(s.passwordSet, true); assert.equal(s.passwordSource, 'saved in IXC'); assert.equal(s.mode, 'on'); assert.equal(s.launch, 'withIxc');
  assert.ok(!(await everything(c)).includes(PW), 'password leaked');
  c = await c.restart(); cleanup.push(() => c.stop()); s = await status(c);   // persistence across a restart
  assert.equal(s.url, 'ws://localhost:9999/'); assert.equal(s.passwordSet, true); assert.equal(s.mode, 'on'); assert.equal(s.launch, 'withIxc'); assert.ok(!(await everything(c)).includes(PW));
  assert.match(c.log('streamerbot'), /settings saved \(url, password, launch, mode\)/); assert.match(c.log('streamerbot'), /settings NOT saved \(fields: url\)/);
});

test('MOCK connection test: success reports version + accounts; wrong password, missing password and a closed server are explained', async () => {
  const sb = await fakeSb();
  const c = await startCore({ env: { IXC_EP_STREAMERBOT: sb.url }, config: sbCfg({ mode: 'off' }) }); cleanup.push(() => c.stop());
  let r = (await api(c, '/api/streamerbot/test', {})).body; assert.equal(r.reachable, true); assert.equal(r.authRequired, true); assert.equal(r.ok, false); assert.match(r.error, /asks for a password and IXC has none/);
  await api(c, '/api/streamerbot/config', { password: 'wrong-password-123' }); r = (await api(c, '/api/streamerbot/test', {})).body; assert.equal(r.ok, false); assert.equal(r.authOk, false); assert.match(r.error, /refused the password/);
  await api(c, '/api/streamerbot/config', { password: PW }); r = (await api(c, '/api/streamerbot/test', {})).body; assert.equal(r.ok, true, JSON.stringify(r)); assert.equal(r.version, '1.0.7'); assert.deepEqual(r.accounts.sort(), ['mykick', 'mytwitch']);
  assert.ok(!JSON.stringify(r).includes(PW)); assert.ok(!(await everything(c)).includes(PW)); assert.ok(!(await everything(c)).includes('wrong-password-123'));
  await sb.srv.close(); r = (await api(c, '/api/streamerbot/test', {})).body; assert.equal(r.ok, false); assert.equal(r.reachable, false); assert.match(r.error, /Could not connect|did not answer/);
});

test('MOCK link: connects, wrong password -> auth state, disconnect -> reconnect; status shows it', async () => {
  const port = await freePort(); let sb = await fakeSb({ port });
  const c = await startCore({ env: { IXC_EP_STREAMERBOT: sb.url }, config: sbCfg({ mode: 'on' }) }); cleanup.push(() => c.stop());
  await until(async () => (await status(c)).link.state === 'auth', 10000, 'wrong/missing password is reported'); assert.match((await status(c)).link.detail, /password/);
  await api(c, '/api/streamerbot/config', { password: PW });
  await until(async () => (await status(c)).link.ready, 15000, 'connected after the password was saved'); let s = await status(c); assert.equal(s.link.auth, 'ok');
  await until(async () => (await status(c)).link.accounts.includes('mykick'), 8000, 'accounts reported');
  sb.chat('kick', 'hello via streamer.bot', 'e1'); await until(async () => (await status(c)).link.events >= 1 && (await status(c)).link.lastEvent, 8000, 'last event time');
  // Streamer.bot closes: IXC reports it, then reconnects by itself when it is back on the same address
  await sb.srv.close(); await until(async () => !(await status(c)).link.ready, 10000, 'disconnect noticed');
  sb = await fakeSb({ port }); await until(async () => (await status(c)).link.ready, 40000, 'reconnected'); assert.equal((await status(c)).link.auth, 'ok');
});

test('MOCK chat through Streamer.bot: events shown once, replies go out once, IXC\'s own reply echo is shown as yours and never runs a command', async () => {
  const sb = await fakeSb();
  const c = await startCore({ env: { IXC_EP_STREAMERBOT: sb.url }, config: sbCfg({ mode: 'on' }) }); cleanup.push(() => c.stop());
  await api(c, '/api/streamerbot/config', { password: PW }); await until(async () => (await status(c)).link.ready, 15000, 'connected');
  const p = await page(c, 'chat', ['chat']);
  sb.chat('kick', 'hello chat', 'dup1'); sb.chat('kick', 'hello chat', 'dup1');   // the same event twice
  await p.wait((m) => m.type === 'chat.msg' && m.m.text === 'hello chat', 8000); await sleep(800);
  assert.ok(!p.msgs.some((m) => m.type === 'chat.msg' && m.m.text === 'hello chat'), 'the duplicate was dropped');
  // a reply: sent through Streamer.bot (no IXC sign-in), echoed back by it; the echo must not be a new incoming message
  const reply = '!np echo test'; const r = (await api(c, '/api/chat/send', { platform: 'kick', message: reply })).body; assert.equal(r.results[0].ok, true, JSON.stringify(r));
  const echo = await p.wait((m) => m.type === 'chat.msg' && m.m.text === reply, 8000); assert.equal(echo.m.me, true, 'shown as your own message');
  await sleep(2500); assert.equal(sb.sends.length, 1, 'no second message was sent: the echo did not run the ! command (' + JSON.stringify(sb.sends.map(x => x.message)) + ')');
});

test('Windows: start/stop only a process IXC started; no second copy; an outside copy is left alone', { skip: !process.env.IXC_CORE_EXE ? 'Windows only' : false }, async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-fakesb-')); const src = path.join(dir, 'p.cs'); const exe = path.join(dir, 'IxcFakeSb.exe');   // not named Streamer.bot: a real Streamer.bot is never touched by this test
  fs.writeFileSync(src, 'class P { static void Main() { System.Threading.Thread.Sleep(600000); } }');
  const fw = path.join(process.env.WINDIR, 'Microsoft.NET', 'Framework64', 'v4.0.30319', 'csc.exe'); execFileSync(fw, ['/nologo', '/target:winexe', '/out:' + exe, src]);
  const alive = (pid) => { try { process.kill(pid, 0); return true; } catch { return false; } };
  const c = await startCore({ env: { IXC_EP_SB_EXE: 'IxcFakeSb.exe' }, config: sbCfg({ mode: 'off' }) }); cleanup.push(() => c.stop());
  assert.equal((await api(c, '/api/streamerbot/config', { exePath: dir })).status, 200);
  let s = await status(c); assert.equal(s.installed, true); assert.equal(s.running, false); assert.ok(s.path.toLowerCase().endsWith('ixcfakesb.exe'), s.path);
  let r = (await api(c, '/api/streamerbot/stop', {})); assert.equal(r.status, 400); assert.match(r.body.error, /not running/);
  r = await api(c, '/api/streamerbot/start', {}); assert.equal(r.status, 200, JSON.stringify(r.body)); await until(async () => (await status(c)).running, 8000, 'started'); s = await status(c); assert.equal(s.managed, true); const pid = s.managedPid; assert.ok(alive(pid));
  r = await api(c, '/api/streamerbot/start', {}); assert.equal(r.status, 400); assert.match(r.body.error, /already running \(started by IXC\)/); await sleep(500); assert.equal((await status(c)).processes, 1, 'duplicate launch prevented');
  const c2 = await c.restart(); cleanup.push(() => c2.stop()); s = await status(c2); assert.equal(s.managed, true, 'IXC still knows it started it after its own restart'); assert.equal(s.managedPid, pid);
  r = await api(c2, '/api/streamerbot/stop', {}); assert.equal(r.status, 400); assert.match(r.body.error, /did not close/, 'a program without a window is asked to close, never killed unasked'); assert.ok(alive(pid));
  r = await api(c2, '/api/streamerbot/stop', { force: true }); assert.equal(r.status, 200, JSON.stringify(r.body)); await until(() => !alive(pid), 8000, 'stopped'); assert.equal((await status(c2)).running, false);
  // a copy IXC did NOT start: Start refuses (already running) and Stop refuses
  const outside = (await import('node:child_process')).spawn(exe, [], { stdio: 'ignore', detached: false }); cleanup.push(async () => { try { outside.kill(); } catch {} });
  await until(async () => (await status(c2)).running, 8000, 'outside copy seen'); s = await status(c2); assert.equal(s.managed, false);
  r = await api(c2, '/api/streamerbot/start', {}); assert.match(r.body.error ?? '', /not started by IXC/); r = await api(c2, '/api/streamerbot/stop', { force: true }); assert.equal(r.status, 400); assert.match(r.body.error, /did not start/); assert.ok(alive(outside.pid), 'the outside copy was not touched');
  outside.kill();
});
