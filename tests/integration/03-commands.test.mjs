// Filters, commands, song requests.
import { test, after, before } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, settings, inject, page, httpServer, json, sleep, until } from './harness.mjs';

let c, yt, p; const cleanup = [];
before(async () => {
  // fake YouTube: oEmbed, watch pages (length / live / embeddable), search results
  const vids = { AAAAAAAAAAA: { title: 'Midnight Drive', author: 'Neon Waves', len: 200 }, BBBBBBBBBBB: { title: 'Ten Hour Loop', author: 'Loops', len: 36000 }, CCCCCCCCCCC: { title: 'Banned Anthem', author: 'Bad Artist', len: 180 },
    DDDDDDDDDDD: { title: 'Live Radio', author: 'Radio', len: 0, live: true }, EEEEEEEEEEE: { title: 'Ocean Lights', author: 'Calm', len: 210 } };
  yt = await httpServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname === '/oembed') { const id = (u.searchParams.get('url').match(/v=([\w-]{11})/) || [])[1]; const v = vids[id]; if (!v) { res.writeHead(404); res.end(); return; } json(res, 200, { title: v.title, author_name: v.author }); return; }
    if (u.pathname === '/watch') { const v = vids[u.searchParams.get('v')]; res.writeHead(200); res.end(v ? `"lengthSeconds":"${v.len}" "playabilityStatus":{"status":"OK"} "author":"${v.author}" ${v.live ? '"isLiveContent":true "isLive":true' : ''}` : ''); return; }
    if (u.pathname === '/results') { const q = u.searchParams.get('search_query').toLowerCase(); const hits = Object.entries(vids).filter(([, v]) => v.title.toLowerCase().includes(q.split(' ')[0]));
      res.writeHead(200); res.end(hits.map(([id, v]) => `"videoRenderer":{"videoId":"${id}","x":1,"title":{"runs":[{"text":"${v.title}"}]},"lengthText":{"accessibility":{"accessibilityData":{"label":"x"}},"simpleText":"${Math.floor(v.len / 60)}:${String(v.len % 60).padStart(2, '0')}"},"ownerText":{"runs":[{"text":"${v.author}"}]}`).join('\n')); return; }
    res.writeHead(404); res.end(); }); cleanup.push(yt.close);
  c = await startCore({ env: { IXC_EP_YOUTUBE_WEB: yt.url } }); cleanup.push(c.stop);
  p = await page(c, 'chat', ['chat', 'music.state']);
});
after(async () => { for (const f of cleanup.reverse()) await f(); });

test('filters: links, caps, banned words, blacklist, duplicates, role filter; mods bypass', async () => {
  await settings(c, { 'chat.filters.bannedWordList': ['scam*', 'badword'], 'chat.filters.blacklist': ['TrollGuy'], 'chat.filters.links': 'hide' });
  let r = await inject(c, { platform: 'twitch', id: 'f1', name: 'A', text: 'check www.example.com now' }); assert.ok(r.flags.includes('link') && r.hidden);
  r = await inject(c, { platform: 'twitch', id: 'f2', name: 'B', text: 'THIS IS SO LOUD WOW AMAZING' }); assert.ok(r.flags.includes('caps') && r.noTts && !r.hidden, 'caps: not read, still shown');
  r = await inject(c, { platform: 'kick', id: 'f3', name: 'C', text: 'free scammers here' }); assert.ok(r.flags.includes('banned word') && r.hidden);
  r = await inject(c, { platform: 'kick', id: 'f4', name: 'TrollGuy', text: 'hi' }); assert.ok(r.hidden);
  r = await inject(c, { platform: 'youtube', id: 'f5', name: 'D', text: 'same thing twice' }); assert.equal(r.flags.length, 0);
  r = await inject(c, { platform: 'youtube', id: 'f6', name: 'D', text: 'same thing twice' }); assert.ok(r.flags.includes('duplicate'));
  r = await inject(c, { platform: 'twitch', id: 'f7', name: 'Mod', mod: true, text: 'MODS CAN SHOUT ALL THEY WANT www.x.com' }); assert.equal(r.flags.length, 0, 'mods bypass');
  await settings(c, { 'chat.filters.minRole': 'subscriber' });
  r = await inject(c, { platform: 'twitch', id: 'f8', name: 'E', text: 'hello' }); assert.ok(r.hidden);
  r = await inject(c, { platform: 'twitch', id: 'f9', name: 'F', sub: true, text: 'hello' }); assert.ok(!r.hidden);
  await settings(c, { 'chat.filters.minRole': 'everyone', 'chat.filters.enabled': false });
  r = await inject(c, { platform: 'twitch', id: 'f10', name: 'A2', text: 'www.example.com' }); assert.equal(r.flags.length, 0, 'filters off');
  await settings(c, { 'chat.filters.enabled': true });
});

test('commands: built-ins, custom commands with variables, permissions, cooldowns, platforms; validation', async () => {
  assert.equal((await api(c, '/api/settings', { patch: { 'commands.list': [{ trigger: 'bad command!', response: 'x' }] } })).status, 400);
  assert.equal((await api(c, '/api/settings', { patch: { 'commands.list': [{ trigger: '!a', response: '' }] } })).status, 400, 'custom needs a response');
  const list = (await api(c, '/api/settings')).body.values['commands.list'];
  list.push({ trigger: '!discord', aliases: ['!dc'], response: 'Join {user} at discord.gg/x ({count})', permission: 'everyone', cooldownSec: 30, platforms: [] });
  list.push({ trigger: '!secret', response: 'mods only', permission: 'moderator', cooldownSec: 0, platforms: [] });
  list.push({ trigger: '!kickonly', response: 'kick!', permission: 'everyone', cooldownSec: 0, platforms: ['kick'] });
  await settings(c, { 'commands.list': list });
  p.clear();
  await inject(c, { platform: 'twitch', id: 'c1', name: 'Viewer', text: '!dc' });
  const rep = await p.wait((m) => m.type === 'chat.reply' && /discord/.test(m.text)); assert.equal(rep.text, 'Join @Viewer at discord.gg/x (1)');
  await inject(c, { platform: 'twitch', id: 'c2', name: 'Viewer2', text: '!discord' }); await sleep(500);
  assert.equal(p.msgs.filter(m => m.type === 'chat.reply' && /discord/.test(m.text)).length, 0, 'cooldown');
  await inject(c, { platform: 'twitch', id: 'c3', name: 'Mod', mod: true, text: '!discord' }); await p.wait((m) => m.type === 'chat.reply' && /\(2\)/.test(m.text), 3000, 'mods skip cooldowns');
  await inject(c, { platform: 'twitch', id: 'c4', name: 'Viewer', text: '!secret' }); await sleep(400); assert.ok(!p.msgs.some(m => m.type === 'chat.reply' && m.text === 'mods only'), 'permission');
  await inject(c, { platform: 'twitch', id: 'c5', name: 'Mod', mod: true, text: '!secret' }); await p.wait((m) => m.type === 'chat.reply' && m.text === 'mods only');
  await inject(c, { platform: 'twitch', id: 'c6', name: 'V', text: '!kickonly' }); await sleep(300); assert.ok(!p.msgs.some(m => m.type === 'chat.reply' && m.text === 'kick!'));
  await inject(c, { platform: 'kick', id: 'c7', name: 'V', text: '!kickonly' }); await p.wait((m) => m.type === 'chat.reply' && m.text === 'kick!');
  await inject(c, { platform: 'kick', id: 'c8', name: 'V', text: '!uptime' }); await p.wait((m) => m.type === 'chat.reply' && /isn't live/.test(m.text));
  await inject(c, { platform: 'kick', id: 'c9', name: 'Mod', mod: true, text: '!ttson' }); await until(async () => (await api(c, '/api/settings')).body.values['tts.on'] === true, 3000, 'mod turned TTS on');
  await settings(c, { 'tts.on': false });
});

test('song requests: limits, duplicates, banned, too long, live, links/search, !wrongsong; queue shows who asked', async () => {
  p.clear(); await inject(c, { platform: 'twitch', id: 'r0', name: 'Early', text: '!sr anything' }); await p.wait((m) => m.type === 'chat.reply' && /turned off/.test(m.text), 3000, 'off by default');
  await settings(c, { 'songRequests.enabled': true, 'songRequests.userCooldownSec': 0, 'songRequests.maxPerUser': 2, 'songRequests.maxDurationSec': 600, 'songRequests.bannedArtists': ['Bad Artist'] });
  p.clear();
  await inject(c, { platform: 'twitch', id: 'r1', name: 'Alex', text: '!sr https://youtu.be/AAAAAAAAAAA' });
  const ok = await p.wait((m) => m.type === 'chat.reply' && /Alex added "Midnight Drive"/.test(m.text), 8000); assert.ok(ok);
  let q = (await api(c, '/api/music/state')).body.queue; assert.equal(q.length, 1); assert.equal(q[0].by, 'Alex'); assert.equal(q[0].platform, 'twitch'); assert.equal(q[0].durationSec, 200);
  await inject(c, { platform: 'twitch', id: 'r2', name: 'Sam', text: '!song https://youtu.be/AAAAAAAAAAA' }); await p.wait((m) => m.type === 'chat.reply' && /already in the queue/.test(m.text), 8000, 'duplicate');
  await inject(c, { platform: 'twitch', id: 'r3', name: 'Sam', text: '!sr https://youtu.be/BBBBBBBBBBB' }); await p.wait((m) => m.type === 'chat.reply' && /too long/.test(m.text), 8000, 'too long');
  await inject(c, { platform: 'twitch', id: 'r4', name: 'Sam', text: '!sr https://youtu.be/CCCCCCCCCCC' }); await p.wait((m) => m.type === 'chat.reply' && /artist can't be requested/.test(m.text), 8000, 'banned artist');
  await inject(c, { platform: 'twitch', id: 'r5', name: 'Sam', text: '!sr https://youtu.be/DDDDDDDDDDD' }); await p.wait((m) => m.type === 'chat.reply' && /live streams/.test(m.text), 8000, 'live refused');
  await inject(c, { platform: 'kick', id: 'r6', name: 'Sam', text: '!sr ocean' }); await p.wait((m) => m.type === 'chat.reply' && /Sam added "Ocean Lights"/.test(m.text), 8000, 'by name');
  await inject(c, { platform: 'kick', id: 'r7', name: 'Sam', text: '!sr https://www.youtube.com/playlist?list=PL123456789012' }); await p.wait((m) => m.type === 'chat.reply' && /one song|playlist/.test(m.text), 8000, 'playlist refused');
  await settings(c, { 'songRequests.permission': 'subscriber' });
  await inject(c, { platform: 'kick', id: 'r8', name: 'NoSub', text: '!sr ocean' }); await p.wait((m) => m.type === 'chat.reply' && /subscribers/.test(m.text), 5000, 'permission');
  await settings(c, { 'songRequests.permission': 'everyone', 'songRequests.allowLinks': false });
  await inject(c, { platform: 'kick', id: 'r9', name: 'Lnk', text: '!sr https://youtu.be/EEEEEEEEEEE' }); await p.wait((m) => m.type === 'chat.reply' && /links aren't allowed/.test(m.text), 5000);
  await settings(c, { 'songRequests.allowLinks': true, 'songRequests.userCooldownSec': 60 });
  await inject(c, { platform: 'kick', id: 'r10', name: 'Fast', text: '!sr ocean' }); await inject(c, { platform: 'kick', id: 'r11', name: 'Fast', text: '!sr midnight' });
  await p.wait((m) => m.type === 'chat.reply' && /request again in/.test(m.text), 8000, 'per-person cooldown');
  q = (await api(c, '/api/music/state')).body.queue; const before = q.length;
  await inject(c, { platform: 'kick', id: 'r12', name: 'Sam', text: '!wrongsong' }); await p.wait((m) => m.type === 'chat.reply' && /removed "Ocean Lights"/.test(m.text), 5000);
  q = (await api(c, '/api/music/state')).body.queue; assert.equal(q.length, before - 1);
  const d = (await api(c, '/api/diag')).body.commands.songRequests; assert.ok(d.accepted >= 2 && d.refused >= 6, JSON.stringify(d));
});
