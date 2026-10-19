// Stage 0: the read-only OBS capability report (what requests, outputs, video settings and Aitum vendors this OBS has).
// Fake OBS here - the real OBS report is a separate, manual run (see IXC_IMPLEMENTATION_STATUS.md). The report must be read-only and must
// never ask OBS for anything that contains a stream key or a password.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os'; import { spawn } from 'node:child_process';
import { startCore, api, fakeObs, build, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const READ_ONLY = new Set(['GetVersion', 'GetOutputList', 'GetOutputStatus', 'GetVideoSettings', 'GetStreamStatus', 'GetRecordStatus', 'GetProfileList', 'GetHotkeyList', 'GetStats', 'CallVendorRequest', 'GetCurrentProgramScene']);

test('capability report: requests, outputs with status, video settings, Aitum vendors, hotkeys - only look-ups, no secrets', async () => {
  const obs = await fakeObs(); cleanup.push(obs.close); const c = await startCore({ config: { obs: { websocketUrl: obs.url } } }); cleanup.push(() => c.stop());
  await until(async () => (await api(c, '/api/obs/state')).body.connected, 10000, 'OBS connected');
  // let the core's own start-up work with OBS (setup / routing) finish first, so only the report's requests are counted
  for (let n = -1, stable = 0; stable < 8;) { await new Promise(r => setTimeout(r, 250)); stable = obs.st.calls.length === n ? stable + 1 : 0; n = obs.st.calls.length; }
  obs.st.calls.length = 0;
  const r = (await api(c, '/api/obs/capabilities')).body;
  assert.equal(r.ok, true); assert.equal(r.readOnly, true); assert.equal(r.obs.obsWebSocketVersion, '5.5.0'); assert.equal(r.obs.platform, 'windows');
  assert.ok(r.requestCount >= 10 && r.requests.includes('CallVendorRequest') && r.interestingRequests.includes('GetOutputList')); assert.equal(r.hasVendorRequest, true); assert.equal(r.hasCanvasRequests, false);
  assert.deepEqual(r.videoSettings, { baseWidth: 1920, baseHeight: 1080, outputWidth: 1920, outputHeight: 1080, fpsNumerator: 60, fpsDenominator: 1 }, 'only the listed fields');
  assert.equal(r.outputCount, 2); const a = r.outputs.find(o => o.name === 'aitum_out_1'); assert.equal(a.kind, 'rtmp_output'); assert.equal(a.active, true); assert.equal(a.status.outputBytes, 123456); assert.equal(a.status.outputCongestion, 0.1);
  assert.equal(r.outputs.find(o => o.name === 'adv_stream').status.outputActive, false);
  assert.equal(r.vendors['aitum-multistream'].registered, true); assert.equal(r.vendors['aitum-multistream'].answer.version, '1.0.8');
  assert.equal(r.vendors['aitum-vertical-canvas'].registered, false); assert.match(r.vendors['aitum-vertical-canvas'].error, /vendor not found/);
  assert.equal(r.hotkeys.count, 3); assert.deepEqual(r.hotkeys.matching, ['Aitum Multistream.Start all']); assert.equal(r.profile.currentProfileName, 'Untitled'); assert.equal(r.stats.activeFps, 60);
  // read-only: every request that went to OBS is a look-up; nothing that carries a key was asked for or copied
    for (const t of new Set(obs.st.calls)) assert.ok(READ_ONLY.has(t), 'unexpected request ' + t); assert.ok(!obs.st.forbidden, 'a key-bearing request was sent');
  const text = JSON.stringify(r); assert.ok(!text.includes('SECRET-STREAM-KEY') && !text.includes('must-not-be-copied') && !text.includes('ignored'));
});

test('capability report without OBS says so; the probe mode writes the report, sends only look-ups, and refuses the real data folder', async () => {
  const c0 = await startCore({}); cleanup.push(() => c0.stop()); const r0 = (await api(c0, '/api/obs/capabilities')).body; assert.equal(r0.ok, false); assert.match(r0.error, /OBS isn't connected/);
  // probe: ixc-core.exe --obs-capabilities <file> --config <scratch>/config.json against a fake OBS
  const obs = await fakeObs(); cleanup.push(obs.close); const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-probe-')); fs.writeFileSync(path.join(dir, 'config.json'), JSON.stringify({ obs: { websocketUrl: obs.url } }));
  const exe = path.join(build(), 'core/ixc-core.exe'); // async on purpose: the fake OBS lives in this process, so a blocking spawn would stop it from answering
  const run = (args) => new Promise((res) => { const p = process.env.IXC_CORE_EXE ? spawn(exe, args) : spawn('mono', [exe].concat(args)); let o = ''; p.stdout.on('data', d => o += d); p.stderr.on('data', d => o += d); const t = setTimeout(() => p.kill(), 60000); p.on('exit', (code) => { clearTimeout(t); res({ status: code, stdout: o, stderr: '' }); }); });
  const out = path.join(dir, 'report.json'); const p = await run(['--obs-capabilities', out, '--config', path.join(dir, 'config.json')]); assert.equal(p.status, 0, p.stderr + p.stdout);
  const rep = JSON.parse(fs.readFileSync(out, 'utf8')); assert.equal(rep.ok, true); assert.equal(rep.outputCount, 2); assert.equal(rep.vendors['aitum-multistream'].registered, true);
  for (const t of new Set(obs.st.calls)) assert.ok(READ_ONLY.has(t), 'unexpected request ' + t); assert.ok(!obs.st.calls.includes('CreateScene') && !obs.st.calls.includes('CreateInput'), 'the probe must not set anything up in OBS');
  // without a scratch --config it refuses (it must never run on IXC's real folder)
  const bad = await run(['--obs-capabilities', path.join(dir, 'x.json')]); assert.equal(bad.status, 64); assert.ok(!fs.existsSync(path.join(dir, 'x.json')));
  // OBS not reachable: exit code 2 and a report that says why
  const out2 = path.join(dir, 'down.json'); fs.writeFileSync(path.join(dir, 'config2.json'), JSON.stringify({ obs: { websocketUrl: 'ws://127.0.0.1:1/' } })); const p2 = await run(['--obs-capabilities', out2, '--config', path.join(dir, 'config2.json')]); assert.equal(p2.status, 2); assert.equal(JSON.parse(fs.readFileSync(out2, 'utf8')).ok, false);
});
