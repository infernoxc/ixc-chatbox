// "Your apps" (Client ID / Client secret) save flow for Twitch, Google/YouTube and Kick. No platform is contacted.
// IDs persist and come back after a reload and a restart; secrets are write-only (only "secretSet" is ever shown) and never reach
// logs, settings, diagnostics, config.json or the API; bad input and a failing store are reported and change nothing.
import { test, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import http from 'node:http';
import { startCore, api } from './harness.mjs';

const cleanup = []; after(async () => { for (const f of cleanup.reverse()) await f(); });
const APPS = {
  twitch: { id: 'abcdefghij0123456789abcd' },
  google: { id: '123456789012-abcdefghijklmnop.apps.googleusercontent.com', secret: 'GOCSPX-TopSecretValue12345' },
  kick: { id: '01ABCDEFGHJKMNPQRS', secret: 'kick-TopSecret-1234567890' } };
const SECRETS = [APPS.google.secret, APPS.kick.secret];
const everything = async (c) => [JSON.stringify((await api(c, '/api/accounts')).body), JSON.stringify((await api(c, '/api/settings')).body), JSON.stringify((await api(c, '/api/diag')).body), JSON.stringify((await api(c, '/api/status')).body),
  fs.readFileSync(path.join(c.dir, 'config.json'), 'utf8'), fs.readFileSync(path.join(c.dir, 'secrets.dat')).toString('latin1'), c.output(), ...fs.readdirSync(path.join(c.dir, 'logs')).map(f => fs.readFileSync(path.join(c.dir, 'logs', f), 'utf8'))].join('\n');
const apps = async (c) => (await api(c, '/api/accounts')).body.apps;

test('valid save per platform: ID is stored and shown again after reload and restart; the secret is write-only', async () => {
  let c = await startCore(); cleanup.push(() => c.stop());
  for (const k of ['twitch', 'google', 'kick']) {
    const r = await api(c, '/api/accounts/apps', { [k]: APPS[k] }); assert.equal(r.status, 200, k + ' ' + JSON.stringify(r.body)); assert.equal(r.body.ok, true);
    assert.equal(r.body.apps[k].id, APPS[k].id); assert.equal(r.body.apps[k].secretSet, !!APPS[k].secret, 'secretSet reflects the secret');
    for (const s of SECRETS) assert.ok(!JSON.stringify(r.body).includes(s), 'the save response never echoes a secret'); }
  let a = await apps(c); for (const k of ['twitch', 'google', 'kick']) { assert.equal(a[k].id, APPS[k].id); assert.equal(a[k].own, true); assert.equal(a[k].available, true); }
  assert.equal(a.twitch.secretSet, false); assert.equal(a.google.secretSet, true); assert.equal(a.kick.secretSet, true);
  assert.ok(!('secret' in a.google) && !('secret' in a.kick), 'no secret field at all');
  c = await c.restart(); cleanup.push(() => c.stop()); a = await apps(c);
  for (const k of ['twitch', 'google', 'kick']) assert.equal(a[k].id, APPS[k].id, k + ' ID survives a restart'); assert.equal(a.kick.secretSet, true);
  const all = await everything(c); for (const s of SECRETS) assert.ok(!all.includes(s), 'secret leaked: ' + s.slice(0, 6) + '...');
  // saving only a new ID leaves the saved secret alone; saving only a secret leaves the ID alone
  await api(c, '/api/accounts/apps', { kick: { id: '01ZZZZZZZZZZZZZZZZ' } }); a = await apps(c); assert.equal(a.kick.id, '01ZZZZZZZZZZZZZZZZ'); assert.equal(a.kick.secretSet, true);
  await api(c, '/api/accounts/apps', { kick: { secret: 'another-secret-0987654321' } }); a = await apps(c); assert.equal(a.kick.id, '01ZZZZZZZZZZZZZZZZ'); assert.equal(a.kick.secretSet, true);
  assert.ok(!(await everything(c)).includes('another-secret-0987654321'));
  // Remove: both cleared
  await api(c, '/api/accounts/apps', { kick: { id: '', secret: '' } }); a = await apps(c); assert.equal(a.kick.id, ''); assert.equal(a.kick.secretSet, false); assert.equal(a.kick.own, false);
});

test('invalid and missing input: refused with a reason, nothing changes, values are not logged', async () => {
  const c = await startCore(); cleanup.push(() => c.stop());
  await api(c, '/api/accounts/apps', { kick: APPS.kick }); const before = JSON.stringify(await apps(c));
  const bad = [[{ twitch: { id: 'NOT A VALID ID!!' } }, /Twitch Client ID/], [{ google: { id: 'abc123' } }, /Google client ID/], [{ kick: { id: 'x' } }, /Kick Client ID/],
    [{ kick: { secret: 'short' } }, /client secret/], [{ kick: { secret: 'has a space inside it 123' } }, /client secret/], [{ google: { id: APPS.google.id, secret: 'x'.repeat(250) } }, /client secret/],
    [{}, /nothing to save/], [{ kick: {} }, /nothing to save/], [{ twitch: { secret: 'ignored-for-twitch-123456' } }, /nothing to save/]];
  for (const [body, re] of bad) { const r = await api(c, '/api/accounts/apps', body); assert.equal(r.status, 400, JSON.stringify(body)); assert.equal(r.body.ok, false); assert.match(r.body.error, re); }
  // a failed save with a valid half must not store the valid half (all or nothing)
  const r = await api(c, '/api/accounts/apps', { google: { id: APPS.google.id, secret: 'short' } }); assert.equal(r.status, 400); assert.equal((await apps(c)).google.id, '', 'half a save is not kept');
  assert.equal(JSON.stringify(await apps(c)), before, 'nothing changed');
  const log = c.log('auth'); assert.match(log, /NOT saved \(HTTP 400; fields: kick\.secret\)/, 'diagnostics name the fields and the result'); assert.doesNotMatch(log, /has a space|NOT A VALID|short/);
  assert.ok(!(await everything(c)).includes('has a space inside it 123'));
});

test('backend failure (the encrypted store cannot be written) is reported, never shown as saved', async () => {
  const c = await startCore(); cleanup.push(() => c.stop());
  await api(c, '/api/accounts/apps', { twitch: APPS.twitch });   // creates secrets.dat
  const sp = path.join(c.dir, 'secrets.dat'), keep = fs.readFileSync(sp); await c.stop(); fs.rmSync(sp); fs.mkdirSync(sp);   // a folder where the file must be: every write fails
  const c2 = await startCore({ dir: c.dir, port: c.port, keepConfig: true }); cleanup.push(() => c2.stop());
  const r = await api(c2, '/api/accounts/apps', { kick: APPS.kick });
  assert.equal(r.status, 400, JSON.stringify(r.body)); assert.equal(r.body.ok, false); assert.match(r.body.error, /couldn't write|could not read it back/);
  const a = await apps(c2); assert.equal(a.kick.id === APPS.kick.id && a.kick.secretSet, false, 'not reported as stored');
  assert.match(c2.log('auth'), /NOT saved/); assert.ok(!(c2.log('auth') + c2.output()).includes(APPS.kick.secret));
  await c2.stop(); fs.rmSync(sp, { recursive: true }); fs.writeFileSync(sp, keep);
});

test('redirect URLs shown for Kick and YouTube are the real callback routes', async () => {
  const c = await startCore(); cleanup.push(() => c.stop()); const a = await apps(c);
  assert.equal(a.kick.redirect, `http://localhost:${c.port}/oauth/kick`); assert.equal(a.google.redirect, `http://127.0.0.1:${c.port}/oauth/youtube`); assert.equal(a.twitch.redirect, '', 'Twitch uses the device code: no redirect');
  for (const p of ['kick', 'youtube']) {
    const res = await new Promise((resolve, rej) => http.get({ host: '127.0.0.1', port: c.port, path: `/oauth/${p}?error=access_denied&state=none` }, (r) => { let b = ''; r.on('data', d => b += d); r.on('end', () => resolve({ code: r.statusCode, body: b })); }).on('error', rej));
    assert.equal(res.code, 200, p + ' callback route exists'); assert.doesNotMatch(res.body, /not found/i); }
});
