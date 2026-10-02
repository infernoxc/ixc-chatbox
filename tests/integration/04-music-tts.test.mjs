// Music queue in IXC (fake player source), local files, TTS queue, per-person voices, ducking with guaranteed restore.
import { test, after, before } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
import { startCore, api, settings, inject, page, httpServer, json, sleep, until } from './harness.mjs';

let c, yt; const cleanup = [];
const vids = { AAAAAAAAAAA: 'Song A', BBBBBBBBBBB: 'Song B', CCCCCCCCCCC: 'Song C', DDDDDDDDDDD: 'Song D' };
before(async () => {
  yt = await httpServer((req, res) => { const u = new URL(req.url, 'http://x');
    if (u.pathname === '/oembed') { const id = (u.searchParams.get('url').match(/v=([\w-]{11})/) || [])[1]; if (!vids[id]) { res.writeHead(404); res.end(); return; } json(res, 200, { title: vids[id], author_name: 'Artist' }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  c = await startCore({ env: { IXC_EP_YOUTUBE_WEB: yt.url }, config: { music: { autoplay: false, repeat: false } } }); cleanup.push(() => c.stop());
});
after(async () => { for (const f of cleanup.reverse()) await f(); });
const player = async () => { const pl = await page(c, 'player', ['music.player']); pl.send({ type: 'music.ev', ev: 'ready', playing: false }); return pl; };
const add = async (id, mode = 'add') => (await api(c, '/api/music/add', { url: 'https://youtu.be/' + id, mode })).body;

test('music: the queue lives in IXC; play, next, errors are skipped, the player can reconnect, nothing is lost on restart', async () => {
  const pl = await player();
  let r = await add('AAAAAAAAAAA'); assert.equal(r.ok, true, JSON.stringify(r));
  let play = await pl.wait('music.play'); assert.equal(play.item.id, 'AAAAAAAAAAA'); assert.equal(play.autoplay, true, 'idle player starts the first song');
  await add('BBBBBBBBBBB'); await add('CCCCCCCCCCC');
  r = await add('AAAAAAAAAAA'); assert.equal(r.code, 'duplicate');
  pl.send({ type: 'music.ev', ev: 'playing', uid: play.item.uid, pos: 0, dur: 200 });
  pl.send({ type: 'music.ev', ev: 'progress', uid: play.item.uid, pos: 42, dur: 200, title: 'Song A (Official)' });
  await until(async () => (await api(c, '/api/music/state')).body.pos >= 42, 3000, 'position reported');
  pl.send({ type: 'music.ev', ev: 'ended', uid: play.item.uid });
  play = await pl.wait('music.play'); assert.equal(play.item.id, 'BBBBBBBBBBB');
  pl.send({ type: 'music.ev', ev: 'error', uid: play.item.uid, code: 150 });   // blocked outside YouTube
  play = await pl.wait('music.play', 5000); assert.equal(play.item.id, 'CCCCCCCCCCC', 'a song that can\'t play is skipped');
  let st = (await api(c, '/api/music/state')).body; assert.equal(st.queue[1].bad, true); assert.match(st.queue[1].why, /blocked/);
  pl.send({ type: 'music.ev', ev: 'playing', uid: play.item.uid, pos: 0, dur: 180 }); pl.send({ type: 'music.ev', ev: 'progress', uid: play.item.uid, pos: 30, dur: 180 });
  await sleep(200);
  // OBS reloads the source: the new page says "ready" and IXC continues where it was
  await pl.close(); const pl2 = await page(c, 'player', ['music.player']); pl2.send({ type: 'music.ev', ev: 'ready', playing: false });
  play = await pl2.wait('music.play'); assert.equal(play.item.id, 'CCCCCCCCCCC'); assert.ok(play.startAt >= 29, 'resumes near 0:30, got ' + play.startAt);
  // queue editing
  assert.equal((await api(c, '/api/music/cmd', { cmd: 'move', from: 2, to: 0 })).status, 200);
  st = (await api(c, '/api/music/state')).body; assert.equal(st.queue[0].id, 'CCCCCCCCCCC'); assert.equal(st.index, 0, 'the current song moved with its index');
  assert.equal((await api(c, '/api/music/cmd', { cmd: 'move', from: 9, to: 0 })).status, 400);
  await api(c, '/api/music/cmd', { cmd: 'pause' }); await pl2.wait((m) => m.type === 'music.ctl' && m.op === 'pause');
  await api(c, '/api/music/cmd', { cmd: 'volume', v: 70 }); await pl2.wait((m) => m.type === 'music.ctl' && m.op === 'volume' && m.v === 70);
  // restart IXC: queue, position and volume survive
  await pl2.close(); await sleep(1000); c = await c.restart(); cleanup.push(() => c.stop());
  st = (await api(c, '/api/music/state')).body; assert.equal(st.queue.length, 3); assert.equal(st.volume, 70);
  const pl3 = await player(); play = await pl3.wait('music.play'); assert.equal(play.autoplay, false, 'not autostarting unless "start with OBS" is on'); assert.equal(play.volume, 70);
  await api(c, '/api/music/cmd', { cmd: 'clear' }); await pl3.wait((m) => m.type === 'music.ctl' && m.op === 'stop');
  await pl3.close();
});

test('music: local files are served only by id, with Range support for seeking', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ixc-music-'));
  // a tiny MP3 with an ID3v2.3 title/artist
  const frame = (id, text) => { const b = Buffer.from([0, ...Buffer.from(text, 'latin1')]); const h = Buffer.alloc(10); h.write(id, 0); h.writeUInt32BE(b.length, 4); return Buffer.concat([h, b]); };
  const frames = Buffer.concat([frame('TIT2', 'Local Hero'), frame('TPE1', 'Garage Band')]); const sz = frames.length; const hdr = Buffer.from([0x49, 0x44, 0x33, 3, 0, 0, (sz >> 21) & 127, (sz >> 14) & 127, (sz >> 7) & 127, sz & 127]);
  fs.writeFileSync(path.join(dir, 'track01.mp3'), Buffer.concat([hdr, frames, Buffer.alloc(5000, 7)]));
  fs.mkdirSync(path.join(dir, 'sub')); fs.writeFileSync(path.join(dir, 'sub', 'Some Artist - Some Title.ogg'), Buffer.alloc(100, 1)); fs.writeFileSync(path.join(dir, 'notes.txt'), 'x');
  assert.equal((await api(c, '/api/music/folders', { path: '/definitely/not/here' })).body.ok, false);
  const r = await api(c, '/api/music/folders', { path: dir }); assert.equal(r.body.ok, true);
  const found = await until(async () => { const s = (await api(c, '/api/music/search?q=hero')).body; return s.find(x => x.kind === 'local') ? s : null; }, 8000, 'local scan');
  const hero = found.find(x => x.kind === 'local'); assert.equal(hero.title, 'Local Hero'); assert.equal(hero.artist, 'Garage Band');
  const s2 = (await api(c, '/api/music/search?q=some title')).body.find(x => x.kind === 'local'); assert.equal(s2.artist, 'Some Artist');
  const full = await fetch(c.url + '/api/music/local/' + hero.local); assert.equal(full.status, 200); assert.equal(full.headers.get('content-type'), 'audio/mpeg'); assert.equal(full.headers.get('accept-ranges'), 'bytes');
  const part = await fetch(c.url + '/api/music/local/' + hero.local, { headers: { Range: 'bytes=10-19' } }); assert.equal(part.status, 206); assert.equal((await part.arrayBuffer()).byteLength, 10); assert.match(part.headers.get('content-range'), /^bytes 10-19\//);
  assert.equal((await fetch(c.url + '/api/music/local/' + hero.local, { headers: { Range: 'bytes=999999-' } })).status, 416);
  assert.equal((await fetch(c.url + '/api/music/local/..%2f..%2fetc%2fpasswd')).status, 404); assert.equal((await fetch(c.url + '/api/music/local/0000000000000000')).status, 404);
  const pl = await player(); const a = await api(c, '/api/music/add', { url: 'local:' + hero.local, mode: 'playnow' }); assert.equal(a.body.ok, true, JSON.stringify(a.body));
  const play = await pl.wait('music.play'); assert.equal(play.item.kind, 'local'); assert.equal(play.item.url, '/api/music/local/' + hero.local);
  await api(c, '/api/music/cmd', { cmd: 'clear' }); await pl.close();
});

test('TTS + music ducking: music fades down while the voice speaks and always comes back', async () => {
  await settings(c, { 'tts.on': true, 'tts.readMode': 'all', 'music.volume': 40, 'music.ducking.level': 25, 'music.ducking.fadeDownMs': 300, 'music.ducking.fadeUpMs': 700, 'tts.userCooldownSec': 0 });
  const pl = await player(); await add('DDDDDDDDDDD'); await pl.wait('music.play');
  const tts = await page(c, 'tts', ['tts.play']); await sleep(300);
  pl.clear();
  await api(c, '/api/tts/say', { text: 'hello there' });
  const duck = await pl.wait((m) => m.type === 'music.ctl' && m.op === 'duck' && m.level < 1, 6000, 'duck down'); assert.equal(duck.level, 0.25); assert.equal(duck.fadeMs, 300);
  const play = await tts.wait('tts.play', 6000); const audio = await fetch(c.url + play.url); assert.equal(audio.status, 200); assert.equal(audio.headers.get('content-type'), 'audio/wav');
  // a second message queued meanwhile: no un-duck between the two
  await api(c, '/api/tts/say', { text: 'second one' }); tts.send({ type: 'tts.done', n: play.n, ok: true });
  const play2 = await tts.wait('tts.play', 6000); assert.ok(!pl.msgs.some(m => m.type === 'music.ctl' && m.op === 'duck' && m.level === 1), 'music stayed down between messages');
  tts.send({ type: 'tts.done', n: play2.n, ok: true });
  const up = await pl.wait((m) => m.type === 'music.ctl' && m.op === 'duck' && m.level === 1, 4000, 'duck back up'); assert.equal(up.fadeMs, 700);
  // skip while speaking -> music comes back
  await api(c, '/api/tts/say', { text: 'skip me' }); const p3 = await tts.wait('tts.play', 6000); await pl.wait((m) => m.type === 'music.ctl' && m.op === 'duck' && m.level < 1, 4000);
  await api(c, '/api/tts/skip', {}); await tts.wait('tts.stop', 3000); await pl.wait((m) => m.type === 'music.ctl' && m.op === 'duck' && m.level === 1, 4000, 'restored after skip');
  // the voice source disappears while speaking -> music comes back
  await api(c, '/api/tts/say', { text: 'lost source' }); await tts.wait('tts.play', 6000); await pl.wait((m) => m.type === 'music.ctl' && m.op === 'duck' && m.level < 1, 4000);
  await tts.close(); await pl.wait((m) => m.type === 'music.ctl' && m.op === 'duck' && m.level === 1, 4000, 'restored when the source closed');
  // ducking off: no ducking
  const tts2 = await page(c, 'tts', ['tts.play']); await settings(c, { 'music.ducking.enabled': false }); pl.clear();
  await api(c, '/api/tts/say', { text: 'no duck' }); const p4 = await tts2.wait('tts.play', 6000); await sleep(300); assert.ok(!pl.msgs.some(m => m.type === 'music.ctl' && m.op === 'duck' && m.level < 1));
  tts2.send({ type: 'tts.done', n: p4.n, ok: true }); await settings(c, { 'music.ducking.enabled': true });
  // a minimum music volume is respected
  await settings(c, { 'music.ducking.level': 0, 'music.ducking.minVolume': 10 }); pl.clear();
  await api(c, '/api/tts/say', { text: 'floor' }); const p5 = await tts2.wait('tts.play', 6000); const d5 = await pl.wait((m) => m.type === 'music.ctl' && m.op === 'duck' && m.level < 1, 4000); assert.equal(d5.level, 0.25, '10% of the slider (40) = 0.25 of the music volume');
  tts2.send({ type: 'tts.done', n: p5.n, ok: true }); await pl.wait((m) => m.type === 'music.ctl' && m.op === 'duck' && m.level === 1, 4000);
  // the music volume itself was never changed by ducking
  assert.equal((await api(c, '/api/music/state')).body.volume, 40);
  await tts2.close(); await api(c, '/api/music/cmd', { cmd: 'clear' }); await pl.close();
});

test('TTS: pause/resume, per-person and per-role voices, filters, numbers are read correctly', async () => {
  await settings(c, { 'tts.on': true, 'tts.readMode': 'all', 'tts.userCooldownSec': 0, 'tts.ignoreOwn': true, 'music.ducking.level': 25, 'music.ducking.minVolume': 3 });
  const tts = await page(c, 'tts', ['tts.play', 'tts']); await sleep(200);
  await settings(c, { 'tts.paused': true }); await tts.wait('tts.pause', 3000);
  await inject(c, { platform: 'twitch', id: 't1', name: 'Alex', text: 'I have 10000 points and 5k coins' }); await sleep(600);
  assert.ok(!tts.msgs.some(m => m.type === 'tts.play'), 'nothing plays while paused');
  await settings(c, { 'tts.paused': false }); const p1 = await tts.wait('tts.play', 5000);
  assert.match(p1.text, /10000 points/, 'numbers are not shortened: ' + p1.text); assert.match(p1.text, /5 thousand/);
  tts.send({ type: 'tts.done', n: p1.n, ok: true });
  assert.equal((await api(c, '/api/tts/user', { name: 'Alex', platform: '*', voice: { voice: 'jarvis', speed: 20, volume: 50 } })).status, 200);
  assert.equal((await api(c, '/api/tts/user', { name: 'Alex', platform: '*', voice: { voice: 'not-a-voice' } })).status, 400);
  await settings(c, { 'tts.roleVoices': { moderator: { voice: 'us-female' } } });
  await inject(c, { platform: 'kick', id: 't2', name: 'Alex', text: 'my own voice now' });
  const p2 = await tts.wait('tts.play', 5000); const st = (await api(c, '/api/tts/state')).body; assert.equal(st.current.voice, 'jarvis'); assert.equal(p2.volume, 50, 'per-person volume');
  tts.send({ type: 'tts.done', n: p2.n, ok: true });
  await inject(c, { platform: 'kick', id: 't3', name: 'ModPerson', mod: true, text: 'moderator voice' }); await tts.wait('tts.play', 5000);
  assert.equal((await api(c, '/api/tts/state')).body.current.voice, 'us-female');
  await api(c, '/api/tts/skip', {});
  await api(c, '/api/tts/user', { name: 'Alex', platform: '*', voice: { enabled: false } });
  await inject(c, { platform: 'kick', id: 't4', name: 'Alex', text: 'muted person' }); await sleep(400);
  assert.ok((await api(c, '/api/tts/state')).body.skipped.some(s => s.reason.includes('turned off for this person')));
  await inject(c, { platform: 'twitch', id: 't5', name: 'Streamer', broadcaster: true, text: 'my own message' }); await sleep(300);
  assert.ok((await api(c, '/api/tts/state')).body.skipped.some(s => s.reason === 'your own account'));
  await settings(c, { 'tts.platforms': ['kick'] }); await inject(c, { platform: 'twitch', id: 't6', name: 'Other', text: 'twitch message' }); await sleep(300);
  assert.ok((await api(c, '/api/tts/state')).body.skipped.some(s => /twitch isn't read/.test(s.reason)));
  // deleted by a moderator before it was read -> not read
  await settings(c, { 'tts.platforms': ['twitch', 'kick', 'youtube', 'rumble'], 'tts.paused': true });
  await inject(c, { platform: 'twitch', id: 't7', name: 'Rude', text: 'something rude' });
  await api(c, '/api/chat/inject', { platform: 'twitch', id: 't8', name: 'x', text: 'y' });
  assert.ok((await api(c, '/api/tts/state')).body.queue.some(q => q.user === 'Rude'));
  await tts.close(); await settings(c, { 'tts.paused': false, 'tts.on': false });
});
