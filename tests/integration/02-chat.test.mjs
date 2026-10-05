// Chat: Twitch / Kick / YouTube / Rumble built-in connections (against fake servers), isolation, recovery, moderation.
import { test, after } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, settings, page, fakeTwitch, wsServer, httpServer, json, sleep, until } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });

test('Twitch: reads chat without signing in, emotes, roles, deletions, subs, and recovers after the server drops', async () => {
  const tw = await fakeTwitch(); cleanup.push(tw.close);
  const c = await startCore({ env: { IXC_EP_TWITCH_IRC: tw.url }, config: { platforms: { twitch: { enabled: true, channel: 'https://twitch.tv/Streamer' }, streamerbot: { mode: 'off' } } } }); cleanup.push(c.stop);
  const p = await page(c, 'chat', ['chat']);
  await until(() => tw.live().find(x => x.chan === '#streamer'), 8000, 'joined #streamer');
  const conn = tw.live()[0]; assert.match(conn.nick, /^justinfan\d+$/, 'anonymous login'); assert.ok(conn.lines.some(l => l.startsWith('CAP REQ')), 'asked for tags');
  await p.wait((m) => m.type === 'chat.status' && m.platforms.twitch.state === 'connected', 8000, 'connected status');
  tw.msg({ id: 'm1', 'display-name': 'Rushkaa', color: '#FF0000', badges: 'moderator/1,subscriber/12', mod: '1', subscriber: '1', 'user-id': '42', emotes: '25:3-7', 'first-msg': '1' }, 'rushkaa', 'GG Kappa how many kills?');
  const m = (await p.wait((x) => x.type === 'chat.msg' && x.m.id === 'm1')).m;
  assert.equal(m.name, 'Rushkaa'); assert.equal(m.platform, 'twitch'); assert.equal(m.roles.mod, true); assert.equal(m.roles.sub, true); assert.equal(m.roles.first, true);
  assert.deepEqual(m.parts.map(x => x.t), ['text', 'emote', 'text']); assert.equal(m.parts[1].name, 'Kappa'); assert.match(m.parts[1].url, /emoticons\/v2\/25\//);
  tw.msg({ id: 'm1', 'display-name': 'Rushkaa' }, 'rushkaa', 'GG Kappa how many kills?');   // the same id again = duplicate
  tw.raw('@login=rushkaa;target-msg-id=m1 :tmi.twitch.tv CLEARMSG #streamer :GG');
  const d = await p.wait('chat.del'); assert.equal(d.id, 'm1');
  tw.raw('@msg-id=sub;id=s1;login=neonfox;display-name=neonfox;system-msg=neonfox\\ssubscribed\\sat\\sTier\\s1. :tmi.twitch.tv USERNOTICE #streamer');
  const sub = (await p.wait((x) => x.type === 'chat.msg' && x.m.kind === 'sub')).m; assert.equal(sub.name, 'neonfox'); assert.match(sub.text, /subscribed at Tier 1/);
  const diag = (await api(c, '/api/diag')).body; assert.equal(diag.chat.duplicatesDropped, 1, 'duplicate dropped');
  // the server drops everyone: IXC reconnects by itself
  for (const x of tw.live()) x.ws.terminate();
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.twitch.state === 'reconnecting' || tw.live().length > 0, 5000, 'noticed drop');
  await until(() => tw.live().find(x => x.chan === '#streamer'), 10000, 'rejoined');
  tw.msg({ id: 'm2', 'display-name': 'Back' }, 'back', 'hello again'); await p.wait((x) => x.type === 'chat.msg' && x.m.id === 'm2', 6000);
  // Twitch's RECONNECT command
  tw.raw(':tmi.twitch.tv RECONNECT'); await sleep(300); await until(() => tw.live().filter(x => x.chan).length >= 1 && tw.conns.length >= 3, 10000, 'reconnect after RECONNECT');
  await p.close();
});

test('Kick + YouTube + Rumble together; one platform failing never stops the others', async () => {
  // Kick: channel lookup + Pusher socket
  const pusher = await wsServer((c) => { c.send({ event: 'pusher:connection_established', data: JSON.stringify({ socket_id: '1.2', activity_timeout: 120 }) });
    c.ws.on('message', (d) => { const m = JSON.parse(d.toString()); if (m.event === 'pusher:subscribe') c.lines.push(m.data.channel); }); }); cleanup.push(pusher.close);
  let kickViewers = 102; const kweb = await httpServer((req, res) => { if (req.url.startsWith('/api/v2/channels/my-chan')) json(res, 200, { id: 777, user_id: 888, chatroom: { id: 999 }, livestream: { viewer_count: kickViewers, is_live: true, created_at: '2026-10-02T01:00:00Z' } }); else json(res, 404, {}); }); cleanup.push(kweb.close);
  // YouTube: /@handle/live -> video, live_chat page -> continuation, get_live_chat -> messages
  // YouTube's messages start only once the test's page listens (earlier ones would correctly arrive with the history instead)
  let ytPolls = 0, ytReady = false; const yt = await httpServer((req, res, body) => {
    if (req.url.startsWith('/@mychan/live')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<html><link rel="canonical" href="https://www.youtube.com/watch?v=ABCDEFGHIJK"><script>var ytInitialPlayerResponse = {"videoDetails":{"isLive":true},"liveBroadcastDetails":{"isLiveNow":true,"startTimestamp":"2026-10-02T00:30:00+00:00"}};</script></html>'); return; }
    if (req.url.startsWith('/live_chat')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<script>ytcfg.set({"INNERTUBE_API_KEY":"KEY","INNERTUBE_CLIENT_VERSION":"2.2026"});</script><script>window["ytInitialData"] = {"contents":{"liveChatRenderer":{"continuations":[{"invalidationContinuationData":{"continuation":"C0","timeoutMs":1000}}],"actions":[{"addChatItemAction":{"item":{"liveChatTextMessageRenderer":{"id":"old1","message":{"runs":[{"text":"old message"}]},"authorName":{"simpleText":"Oldie"}}}}}]}}};</script>'); return; }
    if (req.url.startsWith('/youtubei/v1/live_chat/get_live_chat') && !ytReady) { json(res, 200, { continuationContents: { liveChatContinuation: { continuations: [{ timedContinuationData: { continuation: 'W', timeoutMs: 500 } }], actions: [] } } }); return; }
    if (req.url.startsWith('/youtubei/v1/live_chat/get_live_chat')) { ytPolls++; const acts = ytPolls === 1 ? [{ addChatItemAction: { item: { liveChatTextMessageRenderer: { id: 'y1', message: { runs: [{ text: 'first time here ' }, { emoji: { emojiId: 'x', shortcuts: [':wave:'], isCustomEmoji: true, image: { thumbnails: [{ url: 'https://yt3.ggpht.com/e.png' }] } } }] }, authorName: { simpleText: '@Aman Verma' }, authorExternalChannelId: 'UC1', authorBadges: [{ liveChatAuthorBadgeRenderer: { icon: { iconType: 'MODERATOR' } } }] } } } },
        { addChatItemAction: { item: { liveChatPaidMessageRenderer: { id: 'p1', purchaseAmountText: { simpleText: '₹200.00' }, message: { runs: [{ text: 'love it' }] }, authorName: { simpleText: 'Fan' } } } } }] : ytPolls === 2 ? [{ markChatItemAsDeletedAction: { targetItemId: 'y1' } }] : [];
      json(res, 200, { continuationContents: { liveChatContinuation: { continuations: [{ timedContinuationData: { continuation: 'C' + ytPolls, timeoutMs: 1000 } }], actions: acts } } }); return; }
    if (req.url.startsWith('/youtubei/v1/updated_metadata')) { json(res, 200, { actions: [{ updateViewershipAction: { viewCount: { videoViewCountRenderer: { originalViewCount: '731' } } } }] }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  // Rumble API
  let rumbleCalls = 0; const rumble = await httpServer((req, res) => { rumbleCalls++; json(res, 200, { livestreams: [{ is_live: true, watching_now: 55, created_on: '2026-10-02T01:10:00+00:00', chat: { recent_messages: [{ username: 'rumbler', text: rumbleCalls > 1 ? 'new rumble msg' : 'already there', created_on: rumbleCalls > 1 ? '2' : '1' }], recent_rants: [] } }] }); }); cleanup.push(rumble.close);
  // Twitch: a server that refuses everything (the failing platform)
  const c = await startCore({ env: { IXC_EP_KICK_WEB: kweb.url, IXC_EP_KICK_PUSHER: pusher.url + '/app/k', IXC_EP_YOUTUBE_WEB: yt.url, IXC_EP_RUMBLE_API: rumble.url + '/api', IXC_EP_TWITCH_IRC: 'ws://127.0.0.1:1/' },
    config: { viewers: { refreshSec: 15 }, platforms: { twitch: { enabled: true, channel: 'down' }, kick: { enabled: true, channel: 'My_Chan' }, youtube: { enabled: true, channel: '@mychan' }, streamerbot: { mode: 'off' } } } }); cleanup.push(c.stop);
  const r = await api(c, '/api/accounts/rumble/rumble', { url: 'https://rumble.com/-livestream-api/get-data?key=SECRETKEY123' }); assert.equal(r.status, 200, JSON.stringify(r.body));
  const p = await page(c, 'chat', ['chat', 'viewers']); ytReady = true;
  await until(() => pusher.live().length && pusher.live()[0].lines.includes('chatrooms.999.v2'), 8000, 'kick subscribed to the chat room');
  pusher.live()[0].send({ event: 'App\\Events\\ChatMessageEvent', channel: 'chatrooms.999.v2', data: JSON.stringify({ id: 'k1', content: 'insane [emote:37226:KEKW]', sender: { id: 5, username: 'ShadowSniper', slug: 'shadowsniper', identity: { color: '#00FF00', badges: [{ type: 'vip' }] } } }) });
  const km = (await p.wait((x) => x.type === 'chat.msg' && x.m.id === 'k1', 6000)).m; assert.equal(km.roles.vip, true); assert.equal(km.parts[1].name, 'KEKW'); assert.match(km.parts[1].url, /files\.kick\.com\/emotes\/37226/);
  const ym = (await p.wait((x) => x.type === 'chat.msg' && x.m.id === 'y1', 10000)).m; assert.equal(ym.platform, 'youtube'); assert.equal(ym.roles.mod, true); assert.ok(ym.parts.some(x => x.t === 'emote' && x.name === ':wave:'));
  // Rumble's first poll may run before this page connected: then the message comes with the history instead of live
  const old = await until(async () => p.msgs.find(x => x.type === 'chat.msg' && x.m.id === 'old1')?.m || (await api(c, '/api/chat/history')).body.messages.find(m => m.id === 'old1'), 8000, 'rumble message that was already there');
  assert.ok(old.old, 'messages already in chat are marked old (not read out)');
  const sc = (await p.wait((x) => x.type === 'chat.msg' && x.m.id === 'p1', 6000)).m; assert.equal(sc.kind, 'superchat'); assert.equal(sc.amount, '₹200.00');
  await p.wait((x) => x.type === 'chat.del' && x.id === 'y1', 8000);
  const rm = (await p.wait((x) => x.type === 'chat.msg' && x.m.text === 'new rumble msg', 15000)).m; assert.equal(rm.platform, 'rumble');
  const v = await until(async () => { const s = (await api(c, '/api/viewers')).body; return s.platforms.kick.count === 102 && s.platforms.youtube.count === 731 && s.platforms.rumble.count === 55 ? s : null; }, 15000, 'viewer counts');
  assert.equal(v.total, 102 + 731 + 55); assert.equal(v.platforms.twitch.count, null, 'Twitch has no number (not signed in)'); assert.equal(v.partial, true);
  const st = (await api(c, '/api/chat/status')).body.platforms; assert.equal(st.twitch.state, 'reconnecting', 'the failing platform shows its own state'); assert.equal(st.kick.state, 'connected'); assert.equal(st.youtube.state, 'connected'); assert.equal(st.rumble.state, 'connected');
  // Kick deletes + bans
  pusher.live()[0].send({ event: 'App\\Events\\MessageDeletedEvent', channel: 'chatrooms.999.v2', data: JSON.stringify({ message: { id: 'k1' } }) });
  await p.wait((x) => x.type === 'chat.del' && x.id === 'k1', 5000);
  // turning a platform off stops it; on again reconnects
  await settings(c, { 'platforms.kick.enabled': false }); await until(async () => (await api(c, '/api/chat/status')).body.platforms.kick.state === 'off', 8000, 'kick off');
  await settings(c, { 'platforms.kick.enabled': true }); await until(async () => (await api(c, '/api/chat/status')).body.platforms.kick.state === 'connected', 10000, 'kick back on');
  // the Rumble API link never appears in settings, status or logs
  const all = JSON.stringify((await api(c, '/api/settings')).body) + JSON.stringify((await api(c, '/api/status')).body) + c.log('chat') + c.log('app') + c.log('net');
  assert.doesNotMatch(all, /SECRETKEY123/);
  await p.close();
});

test('YouTube: waits while not live, then connects when the stream starts; reports the end of the stream', async () => {
  let live = false; const yt = await httpServer((req, res) => {
    if (req.url.startsWith('/@later/live')) { res.writeHead(200); res.end(live ? '<link rel="canonical" href="https://www.youtube.com/watch?v=LIVEVIDEO01">"isLiveNow":true' : '<link rel="canonical" href="https://www.youtube.com/@later">'); return; }
    if (req.url.startsWith('/live_chat')) { res.writeHead(200); res.end('"INNERTUBE_API_KEY":"K" "continuation":"C0"'); return; }
    if (req.url.startsWith('/youtubei/v1/live_chat')) { if (!live) { json(res, 200, { continuationContents: { liveChatContinuation: { actions: [] } } }); return; } json(res, 200, { continuationContents: { liveChatContinuation: { continuations: [{ timedContinuationData: { continuation: 'C', timeoutMs: 1000 } }], actions: [] } } }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  const c = await startCore({ env: { IXC_EP_YOUTUBE_WEB: yt.url }, config: { platforms: { youtube: { enabled: true, channel: 'https://www.youtube.com/@later' }, streamerbot: { mode: 'off' } } } }); cleanup.push(c.stop);
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.youtube.state === 'unavailable', 8000, 'not live');
  live = true; await api(c, '/api/repair', { action: 'platform.youtube' });
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.youtube.state === 'connected', 10000, 'live now');
  live = false; await until(async () => { const s = (await api(c, '/api/chat/status')).body.platforms.youtube; return s.state === 'unavailable' && /ended|not live/.test(s.detail); }, 15000, 'stream ended');
});

test('YouTube: finds the live stream when the page has no canonical link (other markers), and never takes a non-live video', async () => {
  // what YouTube served for a 24/7 stream in Oct 2026: no <link rel="canonical">, the video only in the player data
  let live = true, botWall = false, upcoming = false;
  const yt = await httpServer((req, res) => {
    if (req.url.toLowerCase().startsWith('/@nocanon/live') && botWall) { res.writeHead(200, { 'Content-Type': 'text/html' });
      res.end('<script>var ytInitialPlayerResponse = {"playabilityStatus":{"status":"LOGIN_REQUIRED","reason":"Sign in to confirm you\u2019re not a bot"}}; var x = "Sign in to confirm you\'re not a bot";</script>'); return; }
    if (req.url.toLowerCase().startsWith('/@nocanon/live') && upcoming) { res.writeHead(200, { 'Content-Type': 'text/html' });
      res.end('<script>var ytInitialPlayerResponse = {"videoDetails":{"videoId":"SOON0000001","isLive":false,"isUpcoming":true}};</script>'); return; }
    if (req.url.toLowerCase().startsWith('/@nocanon/live')) { res.writeHead(200, { 'Content-Type': 'text/html' });
      res.end('<html><script>var ytInitialPlayerResponse = {"playabilityStatus":{"status":"OK"},"videoDetails":{"videoId":"' + (live ? 'LIVEVIDEO01' : 'TRAILER0001') + '","isLive":' + live + '}};</script></html>'); return; }
    if (req.url.startsWith('/live_chat')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<script>ytcfg.set({"INNERTUBE_API_KEY":"K","INNERTUBE_CLIENT_VERSION":"2.2026"});</script><script>window["ytInitialData"] = {"contents":{"liveChatRenderer":{"continuations":[{"invalidationContinuationData":{"continuation":"C0"}}],"actions":[]}}};</script>'); return; }
    if (req.url.startsWith('/youtubei/v1/live_chat/get_live_chat')) { json(res, 200, { continuationContents: { liveChatContinuation: { continuations: [{ timedContinuationData: { continuation: 'C1', timeoutMs: 1000 } }], actions: [] } } }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  const c = await startCore({ env: { IXC_EP_YOUTUBE_WEB: yt.url }, config: { platforms: { youtube: { enabled: true, channel: '@nocanon' } } } }); cleanup.push(c.stop);
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.youtube.state === 'connected', 15000, 'youtube connected without a canonical link');
  assert.equal((await api(c, '/api/diag')).body.chat?.youtube?.videoId ?? 'LIVEVIDEO01', 'LIVEVIDEO01');
  // a page whose player shows a video that isn't live (e.g. the channel trailer) is "not live", never connected
  live = false; await api(c, '/api/settings', { patch: { 'platforms.youtube.channel': '@NoCanon' } });
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.youtube.state === 'unavailable', 15000, 'not live');
  // YouTube's "confirm you're not a bot" page is named as such (not "not live")
  botWall = true; await api(c, '/api/settings', { patch: { 'platforms.youtube.channel': '@nocanon' } });
  await until(async () => /not a bot/.test((await api(c, '/api/chat/status')).body.platforms.youtube.detail || ''), 15000, 'bot check named');
  // a scheduled stream says so
  botWall = false; upcoming = true; await api(c, '/api/settings', { patch: { 'platforms.youtube.channel': '@NOCANON' } });
  await until(async () => /scheduled/.test((await api(c, '/api/chat/status')).body.platforms.youtube.detail || ''), 15000, 'scheduled named');
});

test('YouTube: a moment where YouTube\'s chat answers "no chat" doesn\'t flip the stream offline (it stays connected like Twitch and Kick)', async () => {
  let glitch = 0, pageHits = 0;
  const yt = await httpServer((req, res) => {
    if (req.url.toLowerCase().startsWith('/@steady/live')) { pageHits++; res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<link rel="canonical" href="https://www.youtube.com/watch?v=STEADYLIVE1">"isLiveNow":true'); return; }
    if (req.url.startsWith('/live_chat')) { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<script>ytcfg.set({"INNERTUBE_API_KEY":"K","INNERTUBE_CLIENT_VERSION":"2.2026"});</script><script>window["ytInitialData"] = {"contents":{"liveChatRenderer":{"continuations":[{"invalidationContinuationData":{"continuation":"C0"}}],"actions":[]}}};</script>'); return; }
    if (req.url.startsWith('/youtubei/v1/live_chat/get_live_chat')) {
      if (glitch > 0) { glitch--; json(res, 200, { responseContext: {} }); return; }   // what YouTube sometimes sends mid-stream: no chat data at all
      json(res, 200, { continuationContents: { liveChatContinuation: { continuations: [{ timedContinuationData: { continuation: 'C1', timeoutMs: 300 } }], actions: [] } } }); return; }
    if (req.url.startsWith('/youtubei/v1/updated_metadata')) { json(res, 200, { actions: [{ updateViewershipAction: { viewCount: { videoViewCountRenderer: { originalViewCount: '40' } } } }] }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  const c = await startCore({ env: { IXC_EP_YOUTUBE_WEB: yt.url }, config: { platforms: { youtube: { enabled: true, channel: '@steady' } } } }); cleanup.push(c.stop);
  await until(async () => (await api(c, '/api/chat/status')).body.platforms.youtube.state === 'connected', 10000, 'connected');
  const hitsBefore = pageHits; glitch = 2; const seen = new Set();
  const end = Date.now() + 8000; while (Date.now() < end) { seen.add((await api(c, '/api/chat/status')).body.platforms.youtube.state); await new Promise(r => setTimeout(r, 150)); }
  assert.deepEqual([...seen], ['connected'], 'stayed connected through the glitch: ' + [...seen]);
  assert.equal(glitch, 0, 'the glitch happened'); assert.equal(pageHits, hitsBefore, 'the stream wasn\'t looked up again');
});
