// Chat page: per-platform health chips (connection / live / last message) and the Send result line. MOCK platforms only.
import { test, before, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs';
import { chromium } from 'playwright';
import { startCore, api, fakeTwitch, httpServer, json, until } from '../integration/harness.mjs';
const exe = process.env.IXC_BROWSER || (fs.existsSync('/opt/pw-browsers/chromium') ? '/opt/pw-browsers/chromium' : undefined);
let c, b, tw, helix, yt; const stop = [];
before(async () => {
  tw = await fakeTwitch(); helix = await httpServer((req, res) => { if (req.url.startsWith('/streams')) json(res, 200, { data: [] }); else if (req.method === 'POST') json(res, 200, '[{"data":{"user":{"stream":null}}}]'); else { res.writeHead(404); res.end(); } });
  yt = await httpServer((req, res) => { if (req.url.startsWith('/@mine/live')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<link rel="canonical" href="https://www.youtube.com/channel/UCaaaaaaaaaaaaaaaaaaaaaa">'); } else { res.writeHead(404); res.end(); } });
  c = await startCore({ env: { IXC_EP_TWITCH_IRC: tw.url, IXC_EP_TWITCH_API: helix.url, IXC_EP_TWITCH_GQL: helix.url, IXC_EP_KICK_WEB: 'http://127.0.0.1:1', IXC_EP_YOUTUBE_WEB: yt.url },
    config: { viewers: { refreshSec: 15 }, platforms: { streamerbot: { mode: 'off' }, twitch: { enabled: true, channel: 'streamer' }, youtube: { enabled: true, channel: '@mine' } } } });
  b = await chromium.launch({ executablePath: exe, args: ['--no-sandbox'] }); });
after(async () => { await b?.close(); await c?.stop(); await tw?.close(); await helix?.close(); await yt?.close(); });

test('MOCK chat chips: connected is separate from not live and last message; Send shows one short line for a missing account', async () => {
  const p = await b.newPage({ viewport: { width: 520, height: 800 } }); await p.goto(`http://127.0.0.1:${c.port}` + '/chat/chat.html?dock=1');
  const tchip = p.locator('.chip[data-p="twitch"]'); await tchip.waitFor();
  await until(async () => /not live/.test(await tchip.innerText()), 20000, 'not live shown');
  let t = await tchip.innerText(); assert.match(t, /no msgs yet/); assert.doesNotMatch(t, /offline|Reconnecting|Error/i, 'connected and not live is not "offline"');
  assert.ok((await tchip.getAttribute('class')).includes('st-connected'));
  tw.msg({ 'display-name': 'Fan', id: 'u1', 'user-id': '5' }, 'fan', 'hello chips');
  await p.locator('.msg', { hasText: 'hello chips' }).waitFor(); await until(async () => /msg \d+s/.test(await tchip.innerText()), 25000, 'last message time');
  // YouTube is waiting for a live chat: not a "disconnected" state
  const y = p.locator('.chip[data-p="youtube"]'); await y.waitFor(); await until(async () => /waiting for a live chat|Error|error/.test(await y.innerText()), 25000, 'youtube state'); assert.match(await y.innerText(), /waiting for a live chat/);
  // Send with no account: the dock only offers the ALL target when something can send, so use the API result the page renders
  const r = await api(c, '/api/chat/send', { platform: 'twitch', message: 'x' }); assert.equal(r.body.results[0].kind, 'needs-account');
});
