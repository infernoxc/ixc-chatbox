// System check, repairs, backups / restore, settings reset, diagnostics export (no secrets), safe updates, restart / quit codes.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import { execFileSync } from 'node:child_process'; import crypto from 'node:crypto';
import { startCore, api, settings, httpServer, json, sleep, until, fakeObs } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const zipText = (f) => execFileSync('python3', ['-c', 'import sys,zipfile; z=zipfile.ZipFile(sys.argv[1]); print("\\n".join(n+"\\n"+z.read(n).decode("utf-8","replace") for n in z.namelist()))', f]).toString();
const waitExit = async (c) => until(() => c.exited != null ? { code: c.exited } : null, 8000, 'IXC exited');

test('system check reports problems with fix actions; repairs run', async () => {
  const c = await startCore(); cleanup.push(() => c.stop());
  const h = (await api(c, '/api/health')).body; assert.ok(['healthy', 'warning', 'error'].includes(h.overall));
  const ids = h.items.map(i => i.id); for (const id of ['core', 'config', 'disk', 'network', 'obs', 'obsws', 'phone', 'update']) assert.ok(ids.includes(id), 'check ' + id);
  const net = h.items.find(i => i.id === 'network'); assert.equal(net.level, 'error', 'no internet in the test = reported, not crashing');
  for (const a of ['connections.all', 'tts.repair', 'overlay.reload', 'cache.clear', 'phone.reconnect', 'streamerbot.reconnect']) { const r = (await api(c, '/api/repair', { action: a })).body; assert.equal(r.ok, true, a + ': ' + JSON.stringify(r)); }
  assert.equal((await api(c, '/api/repair', { action: 'nonsense' })).body.ok, false);
  assert.equal((await api(c, '/api/repair', { action: 'audio.repair' })).body.ok, false, 'audio repair explains OBS isn\'t connected');
});

test('backups: create, restore (restarts IXC), reset settings (backup first)', async () => {
  let c = await startCore(); cleanup.push(() => c.stop());
  await settings(c, { 'tts.voice': 'us-male', 'songRequests.maxQueue': 33 });
  const b = (await api(c, '/api/backups', {})).body; assert.equal(b.ok, true);
  await settings(c, { 'tts.voice': 'uk-female', 'songRequests.maxQueue': 5 });
  const r = (await api(c, '/api/backups', { restore: b.name })).body; assert.equal(r.ok, true);
  assert.equal((await waitExit(c)).code, 3, 'exit code 3 = "restart me" for the tray supervisor');
  c = await c.restart(); cleanup.push(() => c.stop());
  let v = (await api(c, '/api/settings')).body.values; assert.equal(v['tts.voice'], 'us-male'); assert.equal(v['songRequests.maxQueue'], 33);
  assert.ok((await api(c, '/api/backups')).body.backups.some(x => /before-restore/.test(x.name)), 'the state before restoring was backed up too');
  assert.equal((await api(c, '/api/backups', { restore: '../../etc/passwd' })).body.ok, false);
  await api(c, '/api/repair', { action: 'config.reset' }); assert.equal((await waitExit(c)).code, 3);
  c = await c.restart(); cleanup.push(() => c.stop());
  v = (await api(c, '/api/settings')).body.values; assert.equal(v['tts.voice'], 'in-male', 'defaults'); assert.equal(v['general.firstRunDone'], true, 'setup isn\'t forced again');
  assert.ok((await api(c, '/api/backups')).body.backups.some(x => /before-reset/.test(x.name)));
});

test('diagnostics export: logs + system info, but no keys, tokens, IP addresses or the Windows user name', async () => {
  const c = await startCore(); cleanup.push(() => c.stop());
  await settings(c, { 'music.youtubeApiKey': 'AIzaSECRETKEY0001' });
  await api(c, '/api/accounts/rumble/rumble', { url: 'https://rumble.com/-livestream-api/get-data?key=RUMBLESECRET42' });
  await api(c, '/api/music/spotify-secret', { secret: 'abcdefabcdefabcdefabcdefabcdef12' });
  fs.writeFileSync(path.join(c.dir, 'logs', 'phone-sample.log'), 'phone paired: Pixel (203.0.113.77)\nphone connecting from 2001:db8:85a3::8a2e:370:7334\nlistening on 127.0.0.1 and ::1, OS 10.0.19045.0, at 12:34:56\n');
  const r = (await api(c, '/api/diagnostics/export', {})).body; assert.equal(r.ok, true); assert.ok(fs.existsSync(r.path));
  const t = zipText(r.path);
  for (const s of ['203.0.113.77', '2001:db8:85a3::8a2e:370:7334']) assert.ok(!t.includes(s), 'leaked IP ' + s);
  for (const s of ['phone paired: Pixel (<ip>)', 'from <ip>', '127.0.0.1 and ::1', '10.0.19045.0', '12:34:56']) assert.ok(t.includes(s), 'kept / masked: ' + s); assert.match(t, /system\.json/); assert.match(t, /logs\/app\.log/);
  for (const s of ['AIzaSECRETKEY0001', 'RUMBLESECRET42', 'abcdefabcdefabcdefabcdefabcdef12']) assert.ok(!t.includes(s), 'leaked ' + s);
  assert.match(t, /"music\.youtubeApiKey": "\(set\)"/);
});

test('updates: checks GitHub, verifies the download checksum before installing; a damaged download changes nothing', async () => {
  const setup = Buffer.from('fake installer bytes ' + Date.now()); const good = crypto.createHash('sha256').update(setup).digest('hex'); let sum = good;
  const gh = await httpServer((req, res) => {
    if (req.url === '/repos/infernoxc/ixc-chatbox/releases/latest') { json(res, 200, { tag_name: 'v9.9.9', body: 'New things', assets: [{ name: 'IXC-Setup-v9.9.9.exe', browser_download_url: gh.url + '/dl/IXC-Setup-v9.9.9.exe' }, { name: 'SHA256SUMS.txt', browser_download_url: gh.url + '/dl/SHA256SUMS.txt' }] }); return; }
    if (req.url === '/dl/IXC-Setup-v9.9.9.exe') { res.writeHead(200); res.end(setup); return; }
    if (req.url === '/dl/SHA256SUMS.txt') { res.writeHead(200); res.end(sum + '  IXC-Setup-v9.9.9.exe\n'); return; }
    res.writeHead(404); res.end(); }); cleanup.push(gh.close);
  const c = await startCore({ env: { IXC_EP_GITHUB_API: gh.url } }); cleanup.push(() => c.stop());
  const chk = (await api(c, '/api/update/check')).body; assert.equal(chk.available, true); assert.equal(chk.latest, '9.9.9');
  sum = 'f'.repeat(64);
  let r = (await api(c, '/api/repair', { action: 'update.install' })).body; assert.equal(r.ok, true);
  await until(async () => /checksum mismatch/.test((await api(c, '/api/status')).body.update.progress), 10000, 'damaged download refused');
  assert.ok(!fs.existsSync(path.join(c.dir, 'updates', 'IXC-Setup-v9.9.9.exe')), 'nothing kept from a damaged download');
  sum = good; r = (await api(c, '/api/repair', { action: 'update.install' })).body; assert.equal(r.ok, true);
  await until(async () => { const u = (await api(c, '/api/status')).body.update; if (/failed/.test(u.progress)) throw new Error(u.progress); return /verified/.test(u.progress); }, 10000, 'verified');
  assert.ok((await api(c, '/api/backups')).body.backups.some(x => /before-update-9\.9\.9/.test(x.name)), 'backup before updating');
});

test('restart and quit from the dashboard give the supervisor the right exit codes; a second copy exits quietly', async () => {
  const c = await startCore(); cleanup.push(() => c.stop());
  const second = await startCore({ dir: c.dir, port: c.port, keepConfig: true }).catch(e => ({ err: e }));
  if (!second.err) { await until(() => second.exited != null ? true : null, 8000, 'second copy exits'); assert.equal(second.exited, 0, 'a second copy exits quietly'); } else assert.match(String(second.err.message), /exited 0/);
  assert.equal((await api(c, '/api/ping')).status, 200, 'the first copy keeps running');
  await api(c, '/api/system/quit', {}); assert.equal((await waitExit(c)).code, 4);
});

test('updates install by themselves, but not while you are live; one try per version', async () => {
  const setup = Buffer.from('auto installer ' + Date.now()); const sum = crypto.createHash('sha256').update(setup).digest('hex'); let downloads = 0;
  const gh = await httpServer((req, res) => {
    if (req.url === '/repos/infernoxc/ixc-chatbox/releases/latest') { json(res, 200, { tag_name: 'v9.9.8', body: 'Auto', assets: [{ name: 'IXC-Setup-v9.9.8.exe', browser_download_url: gh.url + '/dl/IXC-Setup-v9.9.8.exe' }, { name: 'SHA256SUMS.txt', browser_download_url: gh.url + '/dl/SHA256SUMS.txt' }] }); return; }
    if (req.url === '/dl/IXC-Setup-v9.9.8.exe') { downloads++; res.writeHead(200); res.end(setup); return; }
    if (req.url === '/dl/SHA256SUMS.txt') { res.writeHead(200); res.end(sum + '  IXC-Setup-v9.9.8.exe\n'); return; }
    res.writeHead(404); res.end(); }); cleanup.push(gh.close);
  let live = true; const rumble = await httpServer((req, res) => json(res, 200, { livestreams: [{ is_live: live, watching_now: 5, created_on: '2026-10-05T01:00:00+00:00', chat: { recent_messages: [], recent_rants: [] } }] })); cleanup.push(rumble.close);
  const c = await startCore({ env: { IXC_EP_GITHUB_API: gh.url, IXC_EP_RUMBLE_API: rumble.url + '/api', IXC_EP_UPDATE_TICK_MS: '1000' }, config: { general: { firstRunDone: true, checkUpdates: true } } }); cleanup.push(() => c.stop());
  await api(c, '/api/accounts/rumble/rumble', { url: 'https://rumble.com/-livestream-api/get-data?key=SECRETKEY123' });
  // live on Rumble: the update waits and says why
  await until(async () => /after your stream ends/.test((await api(c, '/api/status')).body.update.autoNote || ''), 15000, 'waits while live');
  assert.equal(downloads, 0, 'nothing downloaded while live');
  // the stream ends: it installs by itself (test mode: downloaded and verified, not run)
  live = false; await until(async () => /verified/.test((await api(c, '/api/status')).body.update.progress || ''), 30000, 'installed by itself after the stream');
  assert.equal(downloads, 1);
  // the same version isn't tried again by itself (a failed installer would otherwise loop)
  await sleep(3000); assert.equal(downloads, 1, 'one automatic try per version');
  assert.match((await api(c, '/api/status')).body.update.autoNote, /Update now/);
});

test('updates never install while OBS is streaming or recording', async () => {
  const setup = Buffer.from('obs installer ' + Date.now()); const sum = crypto.createHash('sha256').update(setup).digest('hex'); let downloads = 0;
  const gh = await httpServer((req, res) => {
    if (req.url === '/repos/infernoxc/ixc-chatbox/releases/latest') { json(res, 200, { tag_name: 'v9.9.7', body: '', assets: [{ name: 'IXC-Setup-v9.9.7.exe', browser_download_url: gh.url + '/dl/IXC-Setup-v9.9.7.exe' }, { name: 'SHA256SUMS.txt', browser_download_url: gh.url + '/dl/SHA256SUMS.txt' }] }); return; }
    if (req.url === '/dl/IXC-Setup-v9.9.7.exe') { downloads++; res.writeHead(200); res.end(setup); return; }
    if (req.url === '/dl/SHA256SUMS.txt') { res.writeHead(200); res.end(sum + '  IXC-Setup-v9.9.7.exe\n'); return; }
    res.writeHead(404); res.end(); }); cleanup.push(gh.close);
  const obs = await fakeObs(); obs.st.streaming = true; cleanup.push(obs.close);
  const c = await startCore({ env: { IXC_EP_GITHUB_API: gh.url, IXC_EP_UPDATE_TICK_MS: '1000' }, config: { obs: { websocketUrl: obs.url }, general: { firstRunDone: true, checkUpdates: true } } }); cleanup.push(() => c.stop());
  await until(async () => /after you stop streaming/.test((await api(c, '/api/status')).body.update.autoNote || ''), 20000, 'waits while streaming');
  obs.st.streaming = false; obs.st.recording = true;
  await until(async () => /after you stop recording/.test((await api(c, '/api/status')).body.update.autoNote || ''), 10000, 'waits while recording');
  assert.equal(downloads, 0, 'nothing downloaded while OBS streams or records');
  obs.st.recording = false;
  await until(async () => /verified/.test((await api(c, '/api/status')).body.update.progress || ''), 30000, 'installs once OBS is idle');
});
