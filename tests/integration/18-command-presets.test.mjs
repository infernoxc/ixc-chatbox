// Ready-made commands: !commands, !followage (Twitch Helix), !clip (Twitch Helix clips, OBS replay buffer elsewhere),
// !discord / !socials (start switched off until links are filled in). Fake Twitch Helix and fake OBS.
import { test, after } from 'node:test'; import assert from 'node:assert/strict';
import { startCore, api, inject, page, httpServer, wsServer, json, fakeTwitch, fakeObs, until, settings } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const replies = (p, re) => p.msgs.filter(m => m.type === 'chat.reply' && re.test(m.text));

test('Presets: added on request, capability-aware answers per platform', async () => {
  let live = true; const clips = [];
  const id = await httpServer((req, res) => {
    if (req.url === '/oauth2/device') { json(res, 200, { device_code: 'dev', user_code: 'ABCD', verification_uri: 'https://www.twitch.tv/activate', interval: 1, expires_in: 60 }); return; }
    if (req.url === '/oauth2/token') { json(res, 200, { access_token: 'tt', refresh_token: 'tr', expires_in: 3600, scope: ['chat:read', 'chat:edit', 'moderator:read:followers', 'clips:edit'] }); return; }
    if (req.url === '/oauth2/validate') { json(res, 200, { login: 'streamer', user_id: '100', scopes: ['chat:read', 'chat:edit', 'moderator:read:followers', 'clips:edit'] }); return; }
    res.writeHead(404); res.end(); }); cleanup.push(id.close);
  const helix = await httpServer((req, res) => {
    if (req.url.startsWith('/channels/followers')) { const u = new URL(req.url, 'http://x'); const who = u.searchParams.get('user_id');
      json(res, 200, { data: who === '555' ? [{ user_id: '555', followed_at: new Date(Date.now() - (400 * 86400000)).toISOString() }] : [], total: 1 }); return; }
    if (req.url.startsWith('/clips')) { clips.push(req.url); if (!live) { json(res, 404, { message: 'Clipping is not possible for an offline channel.' }); return; } json(res, 202, { data: [{ id: 'FunnyClip123', edit_url: 'https://clips.twitch.tv/FunnyClip123/edit' }] }); return; }
    if (req.url.startsWith('/eventsub/subscriptions')) { json(res, 202, { data: [{ id: 's' }] }); return; }
    json(res, 200, { data: [] }); }); cleanup.push(helix.close);
  const es = await wsServer((c) => c.send({ metadata: { message_type: 'session_welcome' }, payload: { session: { id: 's1', keepalive_timeout_seconds: 10 } } })); cleanup.push(es.close);
  const tw = await fakeTwitch(); cleanup.push(tw.close); const obs = await fakeObs(); cleanup.push(obs.close);
  const c = await startCore({ env: { IXC_EP_TWITCH_ID: id.url, IXC_EP_TWITCH_API: helix.url, IXC_EP_TWITCH_IRC: tw.url, IXC_EP_TWITCH_EVENTSUB: es.url },
    config: { obs: { websocketUrl: obs.url }, platforms: { streamerbot: { mode: 'off' } }, commands: { list: [{ trigger: '!np', response: 'Now playing: {song}', builtin: 'np' }] } } }); cleanup.push(() => c.stop());
  // an existing setup keeps its own list; presets show what's missing and add only what's asked for
  let pr = (await api(c, '/api/commands/presets')).body.presets; assert.equal(pr.find(x => x.trigger === '!np').present, true); assert.equal(pr.find(x => x.trigger === '!clip').present, false);
  assert.match(pr.find(x => x.trigger === '!followage').note, /Twitch only/);
  let r = await api(c, '/api/commands/presets', { triggers: ['!commands', '!followage', '!clip', '!discord', '!np'] }); assert.deepEqual(r.body.added, ['!commands', '!followage', '!clip', '!discord']);
  let list = (await api(c, '/api/settings')).body.values['commands.list']; assert.equal(list.length, 5); assert.equal(list.find(x => x.trigger === '!discord').enabled, false, 'text preset waits for the real link');
  r = await api(c, '/api/commands/presets', { triggers: ['!clip'] }); assert.deepEqual(r.body.added, [], 'never twice');
  await settings(c, { 'commands.list': list.map(x => ({ ...x, cooldownSec: 0 })) });   // cooldowns are tested in 03-commands

  const p = await page(c, 'chat', ['chat']);
  // !commands lists what this chatter can use (the switched-off !discord isn't listed)
  await inject(c, { platform: 'kick', id: 'k1', name: 'KickViewer', text: '!commands' });
  await until(() => replies(p, /^Commands:/).length === 1, 5000, '!commands answered'); assert.equal(replies(p, /^Commands:/)[0].text, 'Commands: !np !commands !followage !clip');
  // !followage: Twitch without account -> says so; other platforms -> honest "not available"
  await inject(c, { platform: 'kick', id: 'k2', name: 'KickViewer', text: '!followage' });
  await until(() => replies(p, /only works on Twitch/).length === 1, 5000, 'kick followage refused');
  await inject(c, { platform: 'twitch', id: 't1', name: 'Fan', userId: '555', text: '!followage' });
  await until(() => replies(p, /needs the streamer's Twitch account/).length === 1, 5000, 'no account');
  // !clip without Twitch account and without replay buffer
  await inject(c, { platform: 'youtube', id: 'y1', name: 'YtViewer', text: '!clip' });
  await until(() => replies(p, /replay buffer/).length === 1, 5000, 'no replay buffer'); assert.equal(obs.st.saved, undefined);
  // replay buffer running -> saved in OBS
  obs.st.replay = true; await inject(c, { platform: 'youtube', id: 'y2', name: 'YtViewer2', text: '!clip' });
  await until(() => obs.st.saved === 1, 5000, 'replay saved'); await until(() => replies(p, /Saved a replay clip/).length === 1, 3000, 'said so');

  // connect Twitch
  await api(c, '/api/accounts/apps', { twitch: { id: 'abcdefghij0123456789abcd' } });
  assert.equal((await api(c, '/api/accounts/twitch/connect', {})).body.ok, true);
  await until(async () => (await api(c, '/api/accounts')).body.twitch.account?.login === 'streamer', 10000, 'twitch signed in');
  await settings(c, { 'platforms.twitch.channel': 'streamer' });
  await inject(c, { platform: 'twitch', id: 't2', name: 'Fan', userId: '555', text: '!followage' });
  await until(() => replies(p, /has followed for/).length === 1, 15000, 'followage').catch(e => { throw new Error(e.message + ' ' + JSON.stringify(p.msgs.filter(m => m.type === 'chat.reply').map(m => m.text))); }); assert.equal(replies(p, /has followed for/)[0].text, '@Fan has followed for 1 year 1 month');
  await inject(c, { platform: 'twitch', id: 't3', name: 'Lurker', userId: '666', text: '!followage' });
  await until(() => replies(p, /isn't following yet/).length === 1, 15000, 'not following');   // a loaded CI runner can take longer than 5 s for the Helix round trip + the chat send
  // !clip on Twitch: a real Helix clip for the broadcaster
  await inject(c, { platform: 'twitch', id: 't4', name: 'Fan', userId: '555', text: '!clip' });
  await until(() => replies(p, /clips\.twitch\.tv\/FunnyClip123/).length === 1, 5000, 'clip link'); assert.match(clips[0], /broadcaster_id=100/);
  // offline: Twitch refuses, the replay buffer is used instead (still running)
  live = false; await inject(c, { platform: 'twitch', id: 't5', name: 'Fan2', userId: '557', text: '!clip' });
  await until(() => obs.st.saved === 2, 5000, 'fell back to replay buffer');
  await p.close();
});
