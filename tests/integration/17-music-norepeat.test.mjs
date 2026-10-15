// Music without repeats: shuffle plays every song once before any repeats, Autoplay never adds a song that just played
// (also not another upload of it: lyrics video, "official audio"), history of what played, saved playlists.
// YouTube is a fake server (oEmbed + a watch page with "up next" entries); the player is a fake browser source.
import { test, after, before } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, settings, page, httpServer, json, until } from './harness.mjs';

let c, yt; const cleanup = [];
const vids = { AAAAAAAAAAA: 'Song A', BBBBBBBBBBB: 'Song B', CCCCCCCCCCC: 'Song C', DDDDDDDDDDD: 'Song D', EEEEEEEEEEE: 'Song E' };
const lockup = (id, title, len) => `"lockupViewModel":{"contentId":"${id}","contentType":"LOCKUP_CONTENT_TYPE_VIDEO","metadata":{"title":{"content":"${title}"}},"overlay":{"text":"${len}"}}`;
before(async () => {
  yt = await httpServer((req, res) => { const u = new URL(req.url, 'http://x');
    if (u.pathname === '/oembed') { const id = (u.searchParams.get('url').match(/v=([\w-]{11})/) || [])[1]; if (!vids[id]) { res.writeHead(404); res.end(); return; } json(res, 200, { title: vids[id], author_name: 'Artist' }); return; }
    if (u.pathname === '/watch') {   // "up next": the song itself again under other ids, then two new songs
      res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<html>"secondaryResults":{' + [lockup('XXXXXXXXXX1', 'Song A (Official Lyrics Video)', '3:10'), lockup('XXXXXXXXXX2', 'SONG B [HD] official audio', '3:20'),
        lockup('NNNNNNNNNN1', 'New Song One - Official Audio', '3:30'), lockup('NNNNNNNNNN2', 'New Song One (Lyrics)', '3:30'), lockup('NNNNNNNNNN3', 'New Song Two', '4:00')].join(',') + '}</html>'); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  c = await startCore({ env: { IXC_EP_YOUTUBE_WEB: yt.url }, config: { music: { autoplay: false, repeat: false, shuffle: true } } }); cleanup.push(() => c.stop());
});
after(async () => { for (const f of cleanup.reverse()) await f(); });
const add = async (id) => (await api(c, '/api/music/add', { url: 'https://youtu.be/' + id })).body;

test('shuffle: every song once per round, never twice in a row; history; playlists', async () => {
  const pl = await page(c, 'player', ['music.player']); pl.send({ type: 'music.ev', ev: 'ready', playing: false });
  for (const id of Object.keys(vids)) assert.equal((await add(id)).ok, true);
  const seen = []; let play = await pl.wait('music.play'); seen.push(play.item.id);
  for (let i = 0; i < 4; i++) { pl.send({ type: 'music.ev', ev: 'ended', uid: play.item.uid }); play = await pl.wait('music.play'); seen.push(play.item.id); }
  assert.equal(new Set(seen).size, 5, 'five songs, five different ones: ' + seen.join(' '));
  // Repeat off: after the round the music stops instead of starting over
  pl.send({ type: 'music.ev', ev: 'ended', uid: play.item.uid }); await pl.wait(m => m.type === 'music.ctl' && m.op === 'stop');
  assert.match((await api(c, '/api/music/state')).body.note, /every song played once/);
  // Repeat on: a new round, which doesn't start with the song that just ended
  await settings(c, { 'music.repeat': true }); const last = play.item.id;
  await api(c, '/api/music/cmd', { cmd: 'next' }); play = await pl.wait('music.play'); assert.notEqual(play.item.id, last);
  const round2 = [play.item.id]; for (let i = 0; i < 4; i++) { pl.send({ type: 'music.ev', ev: 'ended', uid: play.item.uid }); play = await pl.wait('music.play'); round2.push(play.item.id); }
  assert.equal(new Set(round2).size, 5, 'second round has no repeats either: ' + round2.join(' '));
  // history: newest first, with titles
  const h = (await api(c, '/api/music/history')).body.played; assert.ok(h.length >= 10); assert.equal(h[0].id, play.item.id); assert.ok(h[0].title.length > 0);
  // playlists: save the queue, clear, load it back; bad names refused
  let r = await api(c, '/api/music/playlists', { op: 'save', name: 'Chill night' }); assert.equal(r.body.ok, true); assert.equal(r.body.songs, 5);
  assert.equal((await api(c, '/api/music/playlists', { op: 'save', name: '' })).status, 400);
  await api(c, '/api/music/cmd', { cmd: 'clear' }); assert.equal((await api(c, '/api/music/state')).body.queue.length, 0);
  r = await api(c, '/api/music/playlists', { op: 'load', name: 'chill NIGHT', mode: 'replace' }); assert.equal(r.body.ok, true, JSON.stringify(r.body));
  play = await pl.wait('music.play'); const st = (await api(c, '/api/music/state')).body; assert.equal(st.queue.length, 5); assert.match(st.queue[0].from, /playlist Chill night/);
  assert.deepEqual((await api(c, '/api/music/playlists')).body.playlists, [{ name: 'Chill night', songs: 5 }]);
  // survives a restart (the queue file is written within a second of a change)
  await pl.close(); await new Promise(r => setTimeout(r, 1200)); c = await c.restart(); cleanup.push(() => c.stop());
  assert.equal((await api(c, '/api/music/playlists')).body.playlists.length, 1); assert.ok((await api(c, '/api/music/history')).body.played.length >= 10, 'history kept');
  r = await api(c, '/api/music/playlists', { op: 'delete', name: 'Chill night' }); assert.equal(r.body.playlists.length, 0);
});

test('autoplay: never re-adds a song that just played, even as another upload', async () => {
  await settings(c, { 'music.shuffle': false, 'music.repeat': false, 'music.autoplay': true });
  await api(c, '/api/music/cmd', { cmd: 'clear' });
  const pl = await page(c, 'player', ['music.player']); pl.send({ type: 'music.ev', ev: 'ready', playing: false });
  await add('AAAAAAAAAAA'); let play = await pl.wait(m => m.type === 'music.play' && m.item.id === 'AAAAAAAAAAA'); await add('BBBBBBBBBBB');
  pl.send({ type: 'music.ev', ev: 'ended', uid: play.item.uid }); play = await pl.wait(m => m.type === 'music.play' && m.item.id === 'BBBBBBBBBBB');
  pl.send({ type: 'music.ev', ev: 'ended', uid: play.item.uid });
  play = await pl.wait(m => m.type === 'music.play' && m.item.auto, 8000);
  const q = (await until(async () => { const s = (await api(c, '/api/music/state')).body; return s.queue.filter(x => x.auto).length ? s : null; }, 5000, 'autoplay added')).queue.filter(x => x.auto);
  assert.deepEqual(q.map(x => x.id), ['NNNNNNNNNN1', 'NNNNNNNNNN3'], 'Song A/B re-uploads and the second copy of New Song One were left out');
  await pl.close();
});
