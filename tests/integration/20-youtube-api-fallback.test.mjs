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
  await until(async () => /not a bot/.test((await api(c, '/api/chat/status')).body.platforms.youtube.detail), 10000, 'bot check reported');
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
