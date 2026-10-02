// Typing must never be interrupted by live updates: the platforms page and the chat box redraw often (status, viewers, chat),
// and a redraw used to replace the text box someone was typing in. This types slowly while IXC pushes updates.
import { test, before, after } from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs';
import { chromium } from 'playwright';
import { startCore, api, inject, settings } from '../integration/harness.mjs';

let c, b; const exe = fs.existsSync('/opt/pw-browsers/chromium') ? '/opt/pw-browsers/chromium' : undefined;
before(async () => { c = await startCore({ config: { general: { firstRunDone: true } } }); b = await chromium.launch({ executablePath: exe, args: ['--no-sandbox'] }); });
after(async () => { await b?.close(); await c?.stop(); });
// keep IXC busy: chat messages and platform changes make it push chat, status and viewer updates while we type
function churn() { let i = 0, on = true; const t = setInterval(async () => { if (!on) return; i++;
  try { await inject(c, { platform: 'twitch', id: 'churn' + i, name: 'Viewer' + (i % 5), text: 'message ' + i });
    if (i % 3 === 0) await settings(c, { 'platforms.youtube.enabled': i % 2 === 0 }); } catch {} }, 250);
  return () => { on = false; clearInterval(t); }; }
async function typeSlowly(p, sel, text) { await p.click(sel); for (const ch of text) { await p.keyboard.type(ch); await p.waitForTimeout(220); } }

test('typing a channel name on the platforms page survives live updates', async () => {
  await settings(c, { 'platforms.kick.enabled': true, 'platforms.kick.channel': '' });
  const p = await b.newPage(); await p.goto(c.url.replace('127.0.0.1', 'localhost') + '/app/#accounts');
  const sel = '[data-k="platforms.kick.channel"]'; await p.waitForSelector(sel);
  const stop = churn(); try { await typeSlowly(p, sel, 'infernoxc_live'); } finally { stop(); }
  assert.equal(await p.inputValue(sel), 'infernoxc_live', 'the text box kept what was typed');
  await p.keyboard.press('Tab');   // leaving the box saves it
  await new Promise(r => setTimeout(r, 1200));
  assert.equal((await api(c, '/api/settings')).body.values['platforms.kick.channel'], 'infernoxc_live');
  await p.close();
});

test('typing in the chat box (OBS panel and the IXC window\'s Chat page) survives incoming chat and status updates', async () => {
  const p = await b.newPage(); await p.goto(c.url.replace('127.0.0.1', 'localhost') + '/chat/chat.html?dock=1'); await p.waitForSelector('#msgIn');
  let stop = churn(); try { await typeSlowly(p, '#msgIn', 'hello chat, how is everyone'); } finally { stop(); }
  assert.equal(await p.inputValue('#msgIn'), 'hello chat, how is everyone');
  await p.goto(c.url.replace('127.0.0.1', 'localhost') + '/app/#chat'); const f = p.frameLocator('iframe.frame'); await f.locator('#msgIn').waitFor();
  stop = churn(); try { await f.locator('#msgIn').click(); for (const ch of 'typing in the app') { await p.keyboard.type(ch); await p.waitForTimeout(220); } } finally { stop(); }
  assert.equal(await f.locator('#msgIn').inputValue(), 'typing in the app');
  await p.close();
});

test('typing a channel name in the setup assistant survives its live refresh', async () => {
  const p = await b.newPage(); await p.goto(c.url.replace('127.0.0.1', 'localhost') + '/app/'); await p.waitForSelector('#navList');
  await p.evaluate(() => Wizard.open(true)); for (let i = 0; i < 2; i++) { await p.click('#wzNext'); await p.waitForTimeout(400); }   // welcome -> OBS -> platforms
  const sel = '#wzCard input[type="text"]'; await p.waitForSelector(sel);
  const stop = churn(); try { await typeSlowly(p, sel, 'mychannel_name'); } finally { stop(); }
  assert.equal(await p.inputValue(sel), 'mychannel_name');
  await p.close();
});
