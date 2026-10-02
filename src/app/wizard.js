// IXC first-run wizard: OBS -> platforms -> sound -> phone -> overlays -> system test -> done.
// Every step fixes what it can by itself and only asks the user for things only they can do (sign in, scan a QR code).
// Part of IXC - (c) 2026 Ishan (InFerNoxC) - MIT License
window.Wizard = (() => {
  const { $, esc, toast, P } = UI; const S = App.S;
  const STEPS = ['welcome', 'obs', 'platforms', 'sound', 'phone', 'overlays', 'test', 'done'];
  let step = 0, shown = false, timer = null, obs = null, check = null;
  function open(force) { if (shown && !force) return; shown = true; step = 0; $('wizard').classList.add('show'); render(); clearInterval(timer); timer = setInterval(refresh, 2000); }
  function close() { $('wizard').classList.remove('show'); clearInterval(timer); }
  async function refresh() { if (STEPS[step] === 'obs') { obs = await IXC.api('/api/obs/detect'); render(); } else if (STEPS[step] === 'platforms' || STEPS[step] === 'sound' || STEPS[step] === 'phone') render(); }
  const line = (ok, title, sub, action) => `<div class="stepline"><span style="font-size:20px">${ok === true ? '✅' : ok === false ? '⚠️' : '⏳'}</span><div class="grow"><b>${title}</b>${sub ? `<small>${sub}</small>` : ''}</div>${action || ''}</div>`;
  function render() {
    const id = STEPS[step]; let h = `<div class="steps">${STEPS.map((_, i) => `<i class="${i <= step ? 'done' : ''}"></i>`).join('')}</div>`;
    const st = S.status || {}; const ch = st.chat || {};
    if (id === 'welcome') h += `<h2 id="wzTitle">Welcome to IXC 👋</h2><p class="muted">Let's get everything ready. IXC sets up OBS, your chat, the chat voice, music and your phone by itself - you'll only be asked to sign in and to press a few buttons.</p>
      ${line(true, 'IXC is running', 'It starts with Windows and lives in the tray (bottom right).')}<p class="hint">Takes about 3 minutes. You can change everything later.</p>`;
    if (id === 'obs') { const o = obs || {}; const c = st.obs || {};
      h += `<h2 id="wzTitle">1. OBS Studio</h2><p class="muted">IXC adds its music player and chat voice to OBS by itself.</p>` +
        line(o.installed ? true : o.installed === false ? false : null, o.installed ? 'OBS Studio found' + (o.version ? ' (' + esc(o.version) + ')' : '') : o.installed === false ? 'OBS Studio isn\'t installed' : 'Looking for OBS…', o.installed === false ? 'Install it from obsproject.com, open it once, then come back.' : '', o.installed === false ? '<a class="btn primary" href="https://obsproject.com/download" target="_blank" rel="noopener">Get OBS</a>' : '') +
        line(o.websocketEnabled || c.connected ? true : o.installed ? false : null, o.websocketEnabled || c.connected ? 'IXC can talk to OBS' : 'IXC needs OBS\'s WebSocket switched on', o.websocketEnabled || c.connected ? '' : o.running ? 'Close OBS (File > Exit) - then press Turn on.' : 'One click - IXC changes the setting for you.', o.websocketEnabled || c.connected ? '' : `<button class="primary" data-fix="obs.websocket" ${o.running ? 'disabled' : ''}>Turn on</button>`) +
        line(c.connected ? true : null, c.connected ? 'Connected to OBS' : 'Open OBS', c.connected ? '' : 'IXC connects as soon as OBS is open.') +
        line(c.setup ? true : c.connected ? false : null, c.setup ? 'IXC sources are in OBS' : 'Add IXC to OBS', c.setup ? 'Scene "IXC Audio" with the music player and chat voice is in all your scenes (invisible on stream).' : 'Music player + chat voice, added to every scene.', c.connected && !c.setup ? '<button class="primary" data-fix="obs.setup">Set up OBS</button>' : '') +
        `<p class="hint">IXC's panels (Chat, Music, IXC) are added to OBS's Docks menu during installation. Missing? System check > "Add IXC panels to OBS" (with OBS closed).</p>`; }
    if (id === 'platforms') { const acc = S.accounts || {};
      h += `<h2 id="wzTitle">2. Your platforms</h2><p class="muted">Connect where you stream. Signing in lets IXC reply in chat, show followers and viewers. Skip any you don't use.</p>` +
        ['twitch', 'kick', 'youtube'].map(p => { const a = (acc[p] || {}).account, fl = (acc[p] || {}).flow, s = ch[p] || {};
          const sub = a ? 'Signed in as ' + esc(a.displayName || a.login) + (s.configured ? ' · chat: ' + esc(UI.STATE[s.state] || s.state) : '') : fl && fl.state === 'waiting' ? esc(fl.message) + (fl.userCode ? ' - code <b>' + esc(fl.userCode) + '</b>' : '') : s.configured ? 'Reading chat of ' + esc(s.channel) + ' (sign in to reply)' : 'Not connected';
          return line(a ? true : s.configured ? true : null, P[p].name, sub, a ? '' : (acc[p] || {}).available ? `<button class="primary" data-connect="${p}">Connect</button>` : `<input type="text" placeholder="channel name" data-chan="${p}" value="${esc(s.channel || '')}" style="width:160px">`); }).join('') +
        line((acc.rumble || {}).account ? true : null, 'Rumble <span class="tiny muted">(optional)</span>', 'Paste your Live Stream API link under Platforms & accounts later.') +
        `<p class="hint">Use Streamer.bot already? IXC uses it too when it's running - nothing to set up.</p>`; }
    if (id === 'sound') { const t = st.tts || {}, m = st.music || {};
      h += `<h2 id="wzTitle">3. Sound check</h2><p class="muted">The chat voice and music play through OBS, so your viewers hear them.</p>` +
        line(t.source ? true : false, 'Chat voice source', t.source ? 'Running in OBS' : 'Not running yet - finish the OBS step (or open OBS).', '<button data-test="1">▶ Test voice</button>') +
        line(m.player ? true : false, 'Music player source', m.player ? 'Running in OBS' : 'Not running yet - finish the OBS step (or open OBS).') +
        `<div class="stepline"><span style="font-size:20px">🔊</span><div class="grow"><b>Read chat out loud</b><small>You can switch it on and off any time from the chat panel or your phone.</small></div><label class="switch"><input type="checkbox" id="wzTts" ${t.on ? 'checked' : ''}><i></i></label></div>
        <div class="stepline"><span style="font-size:20px">🎚</span><div class="grow"><b>Lower music while the voice speaks</b><small>Music fades down and back up smoothly.</small></div><label class="switch"><input type="checkbox" id="wzDuck" ${S.values['music.ducking.enabled'] ? 'checked' : ''}><i></i></label></div>`; }
    if (id === 'phone') { const ph = st.phone || {};
      h += `<h2 id="wzTitle">4. Your phone <span class="tiny muted">(optional)</span></h2><p class="muted">Control music, the chat voice and chat from your phone - on mobile data or any Wi-Fi.</p>` +
        (ph.relay === 'not-configured' ? line(null, 'Not available in this build', 'The phone relay hasn\'t been set up by the developer yet.') : ph.connected ? line(true, 'Phone connected', ph.connected + ' phone(s)') :
          `<div style="text-align:center"><div class="qr" id="wzQr"></div><div class="hint" style="margin-top:8px" id="wzQrNote">${ph.relay === 'connected' ? 'Press "Show QR code", then scan it with your phone\'s camera.' : 'Connecting to the phone relay…'}</div><div class="actions" style="justify-content:center"><button class="primary" id="wzPair" ${ph.relay === 'connected' ? '' : 'disabled'}>Show QR code</button></div></div>`); }
    if (id === 'overlays') h += `<h2 id="wzTitle">5. Overlays</h2><p class="muted">Ready-made overlays for your stream. Add the ones you like to the scene that's on air - you can style them later under Overlays.</p>` +
      [['nowplaying', 'Now Playing', 600, 140], ['chat', 'Chat', 500, 600], ['viewers', 'Viewer counter', 700, 80], ['alerts', 'Alerts', 600, 300]].map(([k, n, w, hh]) => line(null, n, '', `<button data-ov="${k}" data-n="${esc(n)}" data-w="${w}" data-h="${hh}">Add to OBS</button>`)).join('');
    if (id === 'test') { h += `<h2 id="wzTitle">6. System test</h2><p class="muted">IXC checks everything once.</p><div id="wzCheck">${check ? '' : '<div class="hint"><span class="spin"></span> Checking…</div>'}</div>`; }
    if (id === 'done') h += `<h2 id="wzTitle">🎉 Setup complete</h2><p class="muted">IXC is ready for streaming.</p>` + line(true, 'Everything else lives in the dashboard', 'Platforms, overlays, song requests, commands, voices - and System check if anything ever goes wrong.') + line(true, 'In OBS', 'Look under Docks for the IXC Chat and IXC Music panels.');
    h += `<div class="foot">${step > 0 && id !== 'done' ? '<button id="wzBack">Back</button>' : ''}<span class="grow"></span>${id !== 'done' ? '<button class="ghost" id="wzSkip">Skip setup</button>' : ''}<button class="primary" id="wzNext">${id === 'done' ? 'Open dashboard' : id === 'welcome' ? "Let's go" : 'Next'}</button></div>`;
    const keepFocus = document.activeElement && document.activeElement.dataset && document.activeElement.dataset.chan;
    if (keepFocus) return; $('wzCard').innerHTML = h;
    if (id === 'test') { if (!check) runCheck(); else paintCheck(); }
    if (id === 'phone' && pairUrl && $('wzQr')) UI.qr($('wzQr'), pairUrl); }
  let pairUrl = null;
  async function runCheck() { const r = await IXC.api('/api/health'); check = r; paintCheck(); }
  function paintCheck() { const el = $('wzCheck'); if (!el || !check) return; const bad = (check.items || []).filter(i => i.level === 'error' || i.level === 'warn');
    el.innerHTML = (bad.length ? '' : '<div class="banner ok">✓ Everything works</div>') + bad.map(i => line(false, esc(i.name), esc(i.message), i.fix ? `<button class="small" data-fix="${esc(i.fix)}">${esc(i.fixLabel || 'Fix')}</button>` : '')).join('') +
      `<div class="actions"><button id="wzRecheck">Check again</button></div>`; $('wzRecheck').onclick = () => { check = null; render(); }; }
  $('wzCard').addEventListener('click', async (e) => { const b = e.target.closest('button,a'); if (!b) return; const d = b.dataset;
    if (b.id === 'wzNext') { if (STEPS[step] === 'done') { await IXC.api('/api/firstrun/done', {}); close(); location.hash = '#home'; return; } step++; check = null; if (STEPS[step] === 'obs') obs = await IXC.api('/api/obs/detect'); render(); }
    if (b.id === 'wzBack') { step = Math.max(0, step - 1); render(); }
    if (b.id === 'wzSkip') { if (await UI.confirmBox('Skip setup? You can run it again from Settings.', 'Skip')) { await IXC.api('/api/firstrun/done', {}); close(); } }
    if (d.fix) { const r = await App.repair(d.fix, b); if (STEPS[step] === 'obs') { obs = await IXC.api('/api/obs/detect'); render(); } if (STEPS[step] === 'test' && r && r.ok) { check = null; render(); } }
    if (d.connect) { const r = await IXC.api('/api/accounts/' + d.connect + '/connect', {}); if (!r.ok) toast(r.error, true); else toast('Finish signing in in your browser'); }
    if (d.test) App.link.send({ type: 'tts.test' });
    if (b.id === 'wzPair') { const r = await IXC.api('/api/remote/pair', {}); if (!r.ok) return toast(r.error, true); pairUrl = r.url; UI.qr($('wzQr'), r.url); $('wzQrNote').textContent = 'Scan with your phone\'s camera (works once, 5 minutes).'; }
    if (d.ov) { const r = await IXC.api('/api/obs/overlay', { name: d.n, path: '/overlay/' + d.ov + '.html', width: +d.w, height: +d.h }); toast(r.ok ? 'Added to "' + r.scene + '"' : r.error || 'Open OBS first', !r.ok); if (r.ok) b.textContent = '✓ Added'; } });
  $('wzCard').addEventListener('change', async (e) => { const t = e.target;
    if (t.id === 'wzTts') App.save({ 'tts.on': t.checked }); if (t.id === 'wzDuck') App.save({ 'music.ducking.enabled': t.checked });
    if (t.dataset.chan) { const p = t.dataset.chan; await App.save({ ['platforms.' + p + '.channel']: t.value.trim(), ['platforms.' + p + '.enabled']: !!t.value.trim() }); } });
  return { open, close, get shown() { return shown; } };
})();
