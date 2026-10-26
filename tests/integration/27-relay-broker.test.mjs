// MOCK test of the relay's sign-in broker: extracts broker()/json() from relay/src/index.js and runs them with a fake fetch.
import fs from 'node:fs'; import assert from 'node:assert/strict'; import { test } from 'node:test'; import { fileURLToPath } from 'node:url';
test('MOCK relay sign-in broker: no secret, code or token in any error; nothing logged; secret only to the platform token address', async () => {
const src = fs.readFileSync(fileURLToPath(new URL('../../relay/src/index.js', import.meta.url)), 'utf8'); const i = src.indexOf('async function broker'), j = src.indexOf('async function sha');
const code = src.slice(i, j) + '\nreturn { broker };';
const logs = []; const fakeConsole = { log: (...a) => logs.push(a), error: (...a) => logs.push(a), warn: (...a) => logs.push(a) };
let upstream; const mk = new Function('fetch', 'console', code);
const { broker } = mk((url, o) => upstream(url, o), fakeConsole);
const env = { GOOGLE_CLIENT_ID: 'gid', GOOGLE_CLIENT_SECRET: 'TOPSECRET-google', KICK_CLIENT_ID: 'kid', KICK_CLIENT_SECRET: 'TOPSECRET-kick' };
const req = (b) => ({ json: async () => b });
const SECRETS = ['TOPSECRET-google', 'TOPSECRET-kick', 'CODE-123', 'REFRESH-456', 'VERIFIER-789'];
// failure whose upstream body echoes request content: must come back stripped of it
upstream = async (url, o) => new Response(JSON.stringify({ error: 'invalid_grant', error_description: 'Bad code CODE-123 for client_secret=TOPSECRET-google', debug: o.body.toString() }), { status: 400 });
let r = await broker('youtube', req({ grant_type: 'authorization_code', code: 'CODE-123', code_verifier: 'VERIFIER-789', redirect_uri: 'http://127.0.0.1:8767/oauth/youtube' }), env);
let t = await r.text(); assert.equal(r.status, 400); assert.equal(JSON.parse(t).error, 'invalid_grant'); assert.doesNotMatch(t, /debug/);
// the description is text from the platform, bounded to plain characters (no '=' or '&', so no form fields can be replayed)
assert.doesNotMatch(t, /client_secret=/);
// upstream down
upstream = async () => { throw new Error('connect failed to https://oauth2.googleapis.com with TOPSECRET-google'); };
r = await broker('kick', req({ grant_type: 'refresh_token', refresh_token: 'REFRESH-456' }), env); t = await r.text(); assert.equal(r.status, 502); for (const s of SECRETS) assert.ok(!t.includes(s), 'leak: ' + s);
// success passes through; the secret went only to the platform's token address
let sentTo, sentBody; upstream = async (url, o) => { sentTo = url; sentBody = o.body.toString(); return new Response(JSON.stringify({ access_token: 'a', refresh_token: 'b', expires_in: 3600 }), { status: 200 }); };
r = await broker('kick', req({ grant_type: 'refresh_token', refresh_token: 'REFRESH-456' }), env); assert.equal(r.status, 200); assert.equal(sentTo, 'https://id.kick.com/oauth/token'); assert.match(sentBody, /client_secret=TOPSECRET-kick/);
// bad inputs: errors never contain what was sent
r = await broker('youtube', req({ grant_type: 'authorization_code', code: 'CODE-123', redirect_uri: 'https://evil.example/oauth/youtube' }), env); t = await r.text(); assert.equal(r.status, 400); for (const s of SECRETS) assert.ok(!t.includes(s));
r = await broker('youtube', req({ grant_type: 'refresh_token', refresh_token: 'REFRESH-456' }), {}); t = await r.text(); assert.equal(r.status, 503); for (const s of SECRETS) assert.ok(!t.includes(s));
assert.equal(logs.length, 0, 'broker wrote to the console');

});
