// Safe check of Aitum Multistream's obs-websocket start/stop requests against a LOCAL fake ingest (never a platform).
// Run only through AITUM-SINK-TEST.ps1 -Step run, after -Step prepare swapped Aitum's config for one with a single test output.
// Refuses unless Aitum reports exactly that one test output, and unless OBS is neither streaming nor recording. Prints no secrets.
// Part of IXC - Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
import fs from 'node:fs'; import crypto from 'node:crypto'; import path from 'node:path'; import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const { fakeDestination } = await import(new URL('file:///' + path.join(here, '..', 'tests', 'integration', 'rtmp.mjs').replace(/\\/g, '/')));
const TEST = 'IXC Test Sink', PORT = 19360, out = process.argv[2] || path.join(here, 'aitum-test-result.json');
const cfg = JSON.parse(fs.readFileSync(path.join(process.env.APPDATA, 'obs-studio', 'plugin_config', 'obs-websocket', 'config.json'), 'utf8'));
const sha = (s) => crypto.createHash('sha256').update(s).digest('base64'); const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const result = { at: new Date().toISOString(), steps: [], param: null, outputNames: [], events: [], ok: false };
const log = (s) => { console.log(s); result.steps.push(s); };
const ws = new WebSocket('ws://127.0.0.1:' + cfg.server_port, 'obswebsocket.json'); let n = 0; const wait = {}; let ready;
const opened = new Promise(r => { ready = r; });
ws.onmessage = (ev) => { const x = JSON.parse(ev.data);
  if (x.op === 0) { const a = x.d.authentication; ws.send(JSON.stringify({ op: 1, d: { rpcVersion: 1, eventSubscriptions: 512 | 64, authentication: a ? sha(sha(cfg.server_password + a.salt) + a.challenge) : undefined } })); }
  else if (x.op === 2) ready();
  else if (x.op === 5) { if (x.d.eventType === 'VendorEvent' || /Output|Stream/.test(x.d.eventType)) result.events.push({ type: x.d.eventType, data: x.d.eventData && { vendorName: x.d.eventData.vendorName, eventType: x.d.eventData.eventType, eventData: x.d.eventData.eventData } }); }
  else if (x.op === 7 && wait[x.d.requestId]) { wait[x.d.requestId](x.d); delete wait[x.d.requestId]; } };
const call = (t, d) => new Promise(res => { const id = 'r' + (++n); wait[id] = res; ws.send(JSON.stringify({ op: 6, d: { requestType: t, requestId: id, requestData: d || {} } })); setTimeout(() => { if (wait[id]) { delete wait[id]; res({ requestStatus: { result: false, comment: 'timeout' } }); } }, 8000); });
const vendor = (rt, d) => call('CallVendorRequest', { vendorName: 'aitum-multistream', requestType: rt, requestData: d || {} });
const activeOutputs = async () => ((await call('GetOutputList')).responseData?.outputs || []).filter(o => o.outputActive).map(o => o.outputName);
let sink;
try {
  await Promise.race([opened, sleep(8000).then(() => { throw new Error('could not connect to OBS (is it open?)'); })]);
  if ((await call('GetStreamStatus')).responseData?.outputActive || (await call('GetRecordStatus')).responseData?.outputActive) throw new Error('OBS is streaming or recording - not testing');
  const g = await vendor('get_outputs'); const names = (g.responseData?.responseData?.outputs || []).map(o => o.name);
  log('Aitum outputs loaded: ' + JSON.stringify(names));
  if (names.length !== 1 || names[0] !== TEST) throw new Error('Aitum has other outputs loaded (real config?) - refusing. Run -Step prepare with OBS closed first.');
  sink = await fakeDestination({ port: PORT }); log('local fake ingest listening on rtmp://127.0.0.1:' + PORT);
  const before = await activeOutputs();
  for (const param of ['output', 'name', 'outputName']) {
    const r = await vendor('start_output', { [param]: TEST }); log('start_output {' + param + '}: status=' + JSON.stringify(r.requestStatus) + ' answer=' + JSON.stringify(r.responseData?.responseData ?? null));
    for (let i = 0; i < 16 && !sink.live().length; i++) await sleep(500);
    if (sink.live().length) { result.param = param; break; } }
  const during = await activeOutputs(); result.outputNames = during.filter(o => !before.includes(o)); log('OBS outputs active while started: ' + JSON.stringify(during));
  if (!result.param) throw new Error('no parameter name made Aitum publish to the local ingest');
  log('local ingest received a publish (app=' + sink.live()[0].app + ')');
  const s = await vendor('stop_output', { [result.param]: TEST }); log('stop_output: status=' + JSON.stringify(s.requestStatus) + ' answer=' + JSON.stringify(s.responseData?.responseData ?? null));
  for (let i = 0; i < 16 && sink.live().length; i++) await sleep(500);
  const after = await activeOutputs(); log('after stop: ingest live=' + sink.live().length + ', OBS active outputs=' + JSON.stringify(after));
  result.ok = sink.live().length === 0 && !after.some(o => result.outputNames.includes(o));
  log(result.ok ? 'PASS: Aitum start_output/stop_output work with {"' + result.param + '": <output name>}' : 'FAIL: output did not stop cleanly');
} catch (e) { log('STOPPED: ' + e.message); }
finally { try { if (sink && sink.live().length) await vendor('stop_output', { [result.param || 'output']: TEST }); } catch {} fs.writeFileSync(out, JSON.stringify(result, null, 2)); console.log('result written to ' + out); try { ws.close(); } catch {} setTimeout(() => process.exit(0), 500); }
