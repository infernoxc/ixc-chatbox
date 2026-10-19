// YouTube shows its "confirm you're not a bot" page instead of the channel's /live page (some networks and data centres).
// Without a YouTube account IXC says so honestly; with the account connected it asks YouTube's official Data API for the
// active broadcast (liveBroadcasts.list mine=true, at most once a minute) and connects the chat. Fake YouTube web + Google API.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import http from 'node:http';
import { startCore, api, httpServer, json, until, page } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const get = (c, p) => new Promise((res, rej) => http.get({ host: '127.0.0.1', port: c.port, path: p }, (r) => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const BOT = '<html>{"playabilityStatus":{"status":"LOGIN_REQUIRED","reason":"Sign in to confirm you\'re not a bot"}}</html>';

test('YouTube: bot check on the channel page -> official API finds the live broadcast when signed in', async () => {
  let apiCalls = 0, polls = 0;
  const yt = await httpServer((req, res) => {
    if (req.url.startsWith('/@mine/live')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(BOT); return; }
    if (req.url.startsWith('/live_chat') && req.url.includes('v=APILIVE0001')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<script>ytcfg.set({"INNERTUBE_API_KEY":"K","INNERTUBE_CLIENT_VERSION":"2.2026"});</script><script>window["ytInitialData"] = {"contents":{"liveChatRenderer":{"continuations":[{"invalidationContinuationData":{"continuation":"C0"}}],"actions":[]}}};</script>'); return; }
    if (req.url.startsWith('/youtubei/v1/live_chat/get_live_chat')) { polls++; json(res, 200, { continuationContents: { liveChatContinuation: { continuations: [{ timedContinuationData: { continuation: 'C1', timeoutMs: 1000 } }],
      actions: polls === 2 ? [{ addChatItemAction: { item: { liveChatTextMessageRenderer: { id: 'yapi1', message: { runs: [{ text: 'hello from youtube' }] }, authorName: { simpleText: 'YtViewer' }, authorExternalChannelId: 'UCviewer', timestampUsec: String(Date.now() * 1000) } } } }] : [] } } }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  const gtok = await httpServer((req, res) => json(res, 200, { access_token: 'g-tok', refresh_token: 'g-ref', expires_in: 3600, scope: 'https://www.googleapis.com/auth/youtube.force-ssl' })); cleanup.push(gtok.close);
  const gapi = await httpServer((req, res) => {
    if (req.url.startsWith('/channels')) { json(res, 200, { items: [{ id: 'UCmine', snippet: { title: 'My Channel', customUrl: '@mine' } }] }); return; }
    if (req.url.startsWith('/liveBroadcasts')) { apiCalls++; assert.match(req.url, /broadcastStatus=active/); assert.match(req.url, /mine=true/); assert.equal(req.headers.authorization, 'Bearer g-tok');
      json(res, 200, { items: [{ id: 'APILIVE0001', snippet: { liveChatId: 'LC1', actualStartTime: new Date(Date.now() - 600000).toISOString() }, status: { lifeCycleStatus: 'live' } }] }); return; }
    json(res, 404, {}); }); cleanup.push(gapi.close);
  const c = await startCore({ env: { IXC_EP_GOOGLE_TOKEN: gtok.url, IXC_EP_GOOGLE_API: gapi.url, IXC_EP_YOUTUBE_WEB: yt.url },
    config: { platforms: { streamerbot: { mode: 'off' }, youtube: { enabled: true, channel: '@mine' } } } }); cleanup.push(() => c.stop());
  // not signed in: the bot check is reported as such, never as "not live"
  await until(async () => { const y = (await api(c, '/api/chat/status')).body.platforms.youtube; return /not a bot/.test(y.detail) && y.state === 'error'; }, 10000, 'bot check reported');
  assert.equal(apiCalls, 0, 'no API without an account');
  // sign in with the streamer's own Google app
  await api(c, '/api/accounts/apps', { google: { id: '123-abc.apps.googleusercontent.com', secret: 'GOCSPX-google-secret-xyz' } });
  const g = (await api(c, '/api/accounts/youtube/connect', {})).body; const gu = new URL(g.url);
  await get(c, '/oauth/youtube?code=gcode&state=' + gu.searchParams.get('state'));
  await until(async () => (await api(c, '/api/accounts')).body.youtube.account?.displayName === 'My Channel', 5000, 'signed in');
  const p = await page(c, 'chat', ['chat']);
  await api(c, '/api/chat/restart', { platform: 'youtube' }).catch(() => {});
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.youtube.state === 'connected', 70000, 'connected through the API');
  await until(() => p.msgs.some(m => m.type === 'chat.msg' && m.m.text === 'hello from youtube'), 10000, 'chat message read');
  assert.ok(apiCalls >= 1 && apiCalls <= 2, 'API asked once (at most once a minute), got ' + apiCalls);
  await p.close();
});

test('YouTube: Google refusing the token refresh (app in Testing, 7 days) marks the sign-in expired with the Publish-app hint; the chat keeps working without it', async () => {
  const yt = await httpServer((req, res) => { if (req.url.startsWith('/@mine/live')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(BOT); return; } res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  let refused = false, refreshCalls = 0;
  const gtok = await httpServer((req, res, body) => { if (/grant_type=refresh_token/.test(body)) { refreshCalls++; json(res, 400, { error: 'invalid_grant', error_description: 'Token has been expired or revoked.' }); return; }
    json(res, 200, { access_token: 'g-tok', refresh_token: 'g-ref', expires_in: 1, scope: 'https://www.googleapis.com/auth/youtube.force-ssl' }); }); cleanup.push(gtok.close);
  const gapi = await httpServer((req, res) => { if (req.url.startsWith('/channels')) { json(res, 200, { items: [{ id: 'UCmine', snippet: { title: 'My Channel', customUrl: '@mine' } }] }); return; } json(res, 404, {}); }); cleanup.push(gapi.close);
  const c = await startCore({ env: { IXC_EP_GOOGLE_TOKEN: gtok.url, IXC_EP_GOOGLE_API: gapi.url, IXC_EP_YOUTUBE_WEB: yt.url },
    config: { platforms: { streamerbot: { mode: 'off' }, youtube: { enabled: true, channel: '@mine' } } } }); cleanup.push(() => c.stop());
  await api(c, '/api/accounts/apps', { google: { id: '123-abc.apps.googleusercontent.com', secret: 'GOCSPX-google-secret-xyz' } });
  const g = (await api(c, '/api/accounts/youtube/connect', {})).body;
  await get(c, '/oauth/youtube?code=gcode&state=' + new URL(g.url).searchParams.get('state'));
  await until(async () => (await api(c, '/api/accounts')).body.youtube.account?.displayName === 'My Channel', 5000, 'signed in');
  // the next use of the account (finding the live broadcast) tries to refresh the (1 s) token and Google says no
  await api(c, '/api/chat/restart', { platform: 'youtube' }).catch(() => {});
  const acc = await until(async () => { const a = (await api(c, '/api/accounts')).body.youtube.account; return a?.invalid ? a : null; }, 20000, 'sign-in marked expired');
  assert.ok(refreshCalls >= 1, 'a refresh was attempted');
  assert.match(acc.reason, /Publish app/, 'tells the streamer about the 7-day Testing limit');
  assert.match((await api(c, '/api/chat/status')).body.platforms.youtube.detail, /not a bot/, 'chat reading (no sign-in needed) is unaffected');
});

test('YouTube: a genuinely offline channel is "unavailable" (Not live); a wrong name or a page IXC cannot read is an error with the real reason', async () => {
  const yt = await httpServer((req, res) => {
    if (req.url.startsWith('/@offline/live')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<link rel="canonical" href="https://www.youtube.com/channel/UCaaaaaaaaaaaaaaaaaaaaaa">'); return; }
    if (req.url.startsWith('/@consent/live')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<html><title>Before you continue to YouTube</title></html>'); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  const c = await startCore({ env: { IXC_EP_YOUTUBE_WEB: yt.url }, config: { platforms: { streamerbot: { mode: 'off' }, youtube: { enabled: true, channel: '@offline' } } } }); cleanup.push(() => c.stop());
  const y = async () => (await api(c, '/api/chat/status')).body.platforms.youtube; const channel = (ch) => api(c, '/api/settings', { patch: { 'platforms.youtube.channel': ch } });
  await until(async () => { const s = await y(); return s.state === 'unavailable' && /no live stream/.test(s.detail); }, 10000, 'offline channel = not live');
  await channel('@consent'); await until(async () => { const s = await y(); return s.state === 'error' && /wasn't recognised/.test(s.detail); }, 10000, 'unreadable page is an error');
  await channel('@nosuchchannel'); await until(async () => { const s = await y(); return s.state === 'error' && /no channel/.test(s.detail); }, 10000, 'wrong channel name is an error');
  assert.notEqual((await api(c, '/api/viewers')).body.platforms.youtube.state, 'offline', 'viewers are not reported as offline when IXC could not look');
});

test('YouTube: a handle that is exactly 11 characters (infernoxc00) is a channel, not a video id; a bare video id still works', async () => {
  const hits = []; const live = '<link rel="canonical" href="https://www.youtube.com/watch?v=LIVEVIDEO01"><script>var x = {"isLiveNow":true};</script>';
  const yt = await httpServer((req, res) => { hits.push(req.url);
    if (req.url.startsWith('/@infernoxc00/live')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(live); return; }
    if (req.url.startsWith('/watch?v=LIVEVIDEO01')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(live); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  const c = await startCore({ env: { IXC_EP_YOUTUBE_WEB: yt.url }, config: { platforms: { streamerbot: { mode: 'off' }, youtube: { enabled: true, channel: 'infernoxc00' } } } }); cleanup.push(() => c.stop());
  const y = async () => (await api(c, '/api/chat/status')).body.platforms.youtube;
  await until(async () => { const s = await y(); return s.state !== 'unavailable' && s.state !== 'error' && s.state !== 'connecting' ? s : (hits.some(h => h.startsWith('/live_chat')) ? s : null); }, 15000, 'found the live stream through the handle');
  assert.ok(hits[0].startsWith('/@infernoxc00/live'), 'the handle page is asked first, got ' + hits[0]);
  assert.ok(!hits.some(h => h.startsWith('/watch?v=infernoxc00')), 'the handle is not used as a video id');
  hits.length = 0; await api(c, '/api/settings', { patch: { 'platforms.youtube.channel': 'LIVEVIDEO01' } });
  await until(() => hits.some(h => h.startsWith('/watch?v=LIVEVIDEO01')), 15000, 'a bare video id is still read as a video (YouTube has no such channel)');
});
