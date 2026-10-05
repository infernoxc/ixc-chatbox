// Core: security of the local API, settings validation / persistence / corruption recovery, v2 upgrade, static files.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { startCore, api, settings, page, client, sleep, freePort } from './harness.mjs';

const cores = []; after(async () => { for (const c of cores) await c.stop(); });
async function core(o) { const c = await startCore(o); cores.push(c); return c; }

test('local API refuses other websites (null origin, foreign origin, DNS rebinding)', async () => {
  const c = await core();
  assert.equal((await api(c, '/api/tts/say', { text: 'x' }, { Origin: 'null' })).status, 403, 'null origin (sandboxed iframe / file://) must be refused');
  assert.equal((await api(c, '/api/tts/say', { text: 'x' }, { Origin: 'https://evil.example' })).status, 403);
  assert.equal((await api(c, '/api/tts/say', { text: 'x' }, { Origin: 'http://localhost:' + c.port })).status, 200, 'IXC\'s own pages are allowed');
  assert.equal((await api(c, '/api/tts/say', { text: 'x' }, { Origin: 'http://localhost:9999' })).status, 403, 'another local port is a different site');
  const http = await import('node:http');
  const code = await new Promise((res) => http.get({ host: '127.0.0.1', port: c.port, path: '/api/ping', headers: { Host: 'evil.example:' + c.port } }, (r) => { r.resume(); res(r.statusCode); }));
  assert.equal(code, 421, 'a foreign Host header (DNS rebinding) is refused');
  await assert.rejects(client('ws://127.0.0.1:' + c.port + '/ws', null, { headers: { Origin: 'null' } }), /403/);
  await assert.rejects(client('ws://127.0.0.1:' + c.port + '/ws', null, { headers: { Origin: 'https://evil.example' } }), /403/);
  const ok = await client('ws://127.0.0.1:' + c.port + '/ws', { type: 'hello', role: 'x', topics: [] }, { headers: { Origin: 'http://localhost:' + c.port } }); await ok.wait('hello'); await ok.close();
});

test('static files: no path escapes, only known types', async () => {
  const c = await core();
  for (const p of ['/chat/../core/ixc-core.exe', '/chat/%2e%2e/core/ixc-core.exe', '/core/..%2f..%2fconfig.json', '/chat/..\\..\\config.json', '/core/ixc-core.exe', '/music/%00player.html'])
    assert.equal((await fetch(c.url + p)).status, 404, p);
  assert.equal((await fetch(c.url + '/music/player.html')).status, 200);
  assert.equal((await fetch(c.url + '/app/')).status, 200, 'dashboard index');
  assert.equal((await fetch(c.url + '/overlay/nowplaying.html')).status, 200);
  const r = await fetch(c.url + '/', { redirect: 'manual' }); assert.equal(r.status, 302); assert.match(r.headers.get('location'), /\/app\//);
});

test('settings: validation, clamping, unknown keys, persistence across restart', async () => {
  let c = await core();
  assert.equal((await api(c, '/api/settings', { patch: { 'tts.volume': 'loud' } })).status, 400);
  assert.equal((await api(c, '/api/settings', { patch: { 'nope.key': 1 } })).status, 400);
  assert.equal((await api(c, '/api/settings', { patch: { 'tts.readMode': 'everything' } })).status, 400);
  assert.equal((await api(c, '/api/settings', { patch: { 'overlays.chat': { font: '<script>' } } })).status, 400, 'no markup in overlay styles');
  const v = await settings(c, { 'tts.volume': 250, 'tts.speed': -999, 'chat.filters.bannedWordList': 'a,b\nc' });
  assert.equal(v['tts.volume'], 100); assert.equal(v['tts.speed'], -50); assert.deepEqual(v['chat.filters.bannedWordList'], ['a', 'b', 'c']);
  c = await c.restart(); cores.push(c);
  const after2 = (await api(c, '/api/settings')).body.values; assert.equal(after2['tts.volume'], 100); assert.deepEqual(after2['chat.filters.bannedWordList'], ['a', 'b', 'c']);
  // null = back to default
  const d = await settings(c, { 'tts.volume': null }); assert.equal(d['tts.volume'], 100);
});

test('a damaged config.json never stops IXC: last good copy is restored and the problem is reported', async () => {
  const c = await core(); await settings(c, { 'tts.voice': 'us-female' }); await sleep(900); await settings(c, { 'tts.volume': 55 }); await sleep(900);
  await c.stop(); fs.writeFileSync(path.join(c.dir, 'config.json'), '{ this is not json');
  const c2 = await c.restart(); cores.push(c2);
  const st = (await api(c2, '/api/status')).body; assert.ok(st.issues.some(i => i.id === 'config'), 'reported in status');
  const v = (await api(c2, '/api/settings')).body.values; assert.equal(v['tts.voice'], 'us-female', 'restored from backup');
  assert.ok(fs.readdirSync(c.dir).some(f => f.startsWith('config.json.damaged-')), 'damaged file kept');
  // an empty file too
  await c2.stop(); fs.writeFileSync(path.join(c.dir, 'config.json'), ''); const c3 = await c2.restart(); cores.push(c3);
  assert.equal((await api(c3, '/api/ping')).status, 200);
});

test('upgrading a v2 config: passwords move to the encrypted store, old keys are cleaned, setup is not shown again', async () => {
  const v2 = { _readme: 'x', version: '2.0.1', helper: { port: 0 }, tts: { on: true, voice: 'jarvis' }, streamerbot: { websocketUrl: 'ws://127.0.0.1:8080/', password: 'sbSecretPass123', settingsPath: 'auto' },
    music: { routing: { mode: 'kick' }, spotify: { clientId: 'abc', clientSecret: 'zzzzzzzzzzzzzzzzzzzzzzzzzzzzzz' } }, remote: { enabled: true, port: 8769, sessionHours: 12, idleMinutes: 30, allowDownload: true, cloudflaredPath: '' }, obs: { websocketUrl: 'ws://127.0.0.1:1/' } };
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-v2-')); const port = await freePort(); v2.helper.port = port;   // a free port, so another program can't take it first
  fs.writeFileSync(path.join(dir, 'config.json'), JSON.stringify(v2));
  const c = await core({ dir, port, keepConfig: true });
  const cfg = fs.readFileSync(path.join(dir, 'config.json'), 'utf8');
  assert.doesNotMatch(cfg, /sbSecretPass123|zzzzzzzz/, 'secrets removed from config.json');
  assert.ok(fs.existsSync(path.join(dir, 'secrets.dat'))); assert.doesNotMatch(fs.readFileSync(path.join(dir, 'secrets.dat')).toString('latin1'), /sbSecretPass123/, 'secrets file is encrypted');
  const v = (await api(c, '/api/settings')).body.values; assert.equal(v['tts.voice'], 'jarvis'); assert.equal(v['music.routing.mode'], 'kick'); assert.equal(v['general.firstRunDone'], true);
  assert.doesNotMatch(cfg, /cloudflaredPath|sessionHours/);
});

test('every page gets ordered pushes and a stuck client cannot grow memory', async () => {
  const c = await core(); const p = await page(c, 'chat', ['chat']); await p.wait('chat.history');
  for (let i = 0; i < 60; i++) await api(c, '/api/chat/inject', { platform: 'twitch', id: 'o' + i, name: 'u' + (i % 7), text: 'message number ' + i });
  await p.wait((m) => m.type === 'chat.msg' && m.m.text === 'message number 59', 8000);
  const nums = p.msgs.filter(m => m.type === 'chat.msg').map(m => +m.m.text.split(' ').pop()); assert.deepEqual(nums, [...nums].sort((a, b) => a - b), 'messages arrive in order');
  await p.close();
});
