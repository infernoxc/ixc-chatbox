// IXC dashboard - pages. Part of IXC - (c) 2026 Ishan (InFerNoxC) - MIT License
(() => {
  const { $, esc, toast, P } = UI; const { S, ctl, page, on, repair } = App;
  const card = (title, body, cls) => `<div class="card ${cls || ''}"><h3>${title}</h3>${body}</div>`;
  const plats = ['twitch', 'kick', 'youtube', 'rumble'];
  // The read-only OBS report (what this OBS reports about itself). One request per 15 s at most; every page that shows it says so.
  const Caps = { data: null, at: 0, busy: null,
    async get(force) { if (!force && this.data && Date.now() - this.at < 15000) return this.data; if (this.busy) return this.busy;
      this.busy = IXC.api('/api/obs/capabilities').then(r => { this.data = r; this.at = Date.now(); return r; }).catch(e => { this.data = { ok: false, error: String(e.message || e) }; this.at = Date.now(); return this.data; }).finally(() => { this.busy = null; }); return this.busy; },
    vendor(name) { const v = this.data && this.data.vendors && this.data.vendors[name]; return v || null; },
    bytes(n) { n = +n || 0; return n >= 1048576 ? (n / 1048576).toFixed(1) + ' MB' : n >= 1024 ? Math.round(n / 1024) + ' KB' : n + ' B'; },
    down(c, btn) { return '<div class="banner">OBS information is not available: ' + esc((c && c.error) || 'OBS is not connected') + '. Open OBS (IXC connects by itself) or check <a href="#obs">OBS Control</a>.</div>' + (btn ? '<div class="actions"><button class="small" ' + btn + '>Refresh</button></div>' : ''); },
    outputsTable(c) { return c.outputs && c.outputs.length ? '<table class="t"><tr><th>OBS output</th><th>Kind</th><th>State</th><th>Sent</th><th>Dropped / total frames</th></tr>' + c.outputs.map(o => { const st = o.status || {};
        return '<tr><td><b>' + esc(o.name) + '</b></td><td class="small">' + esc(o.kind) + '</td><td>' + UI.pill(o.active ? (st.outputReconnecting ? 'reconnecting' : 'live') : 'off', o.active ? (st.outputReconnecting ? 'Reconnecting' : 'Active') : 'Idle') + '</td><td class="small">' + (o.active ? Caps.bytes(st.outputBytes) : +st.outputBytes ? 'last stream: ' + Caps.bytes(st.outputBytes) : '-') + '</td><td class="small">' + (+st.outputTotalFrames ? (st.outputSkippedFrames || 0) + ' / ' + st.outputTotalFrames : '-') + '</td></tr>'; }).join('') + '</table>' : '<p class="muted">OBS lists no outputs.</p>'; } };
  const NOTIMPL = (t) => '<span class="pill st-unavailable"><i></i>Not implemented</span> <span class="small muted">' + t + '</span>';

  // ======================= HOME =======================
  page('home', 'Overview', (main) => {
    main.innerHTML = `<div id="hBanners"></div><div class="grid" id="hGrid"></div>`;
    const paint = () => {
      const st = S.status; if (!st) { $('hGrid').innerHTML = '<div class="muted">Waiting for IXC…</div>'; return; }
      let b = '';
      if (st.firstRun) b += `<div class="banner info section">👋 Welcome! IXC isn't fully set up yet. <span class="grow"></span><button class="primary" id="hWiz">Start setup</button></div>`;
      if (st.update && st.update.available) b += `<div class="banner info section">⬆️ IXC ${esc(st.update.latest)} is available (you have ${esc(st.update.current)}). <span class="grow"></span><button class="primary" data-fix="update.install">${st.update.busy ? esc(st.update.progress) : 'Update now'}</button></div>`;
      (st.issues || []).forEach(i => { b += `<div class="banner section ${i.level === 'error' ? 'err' : ''}">⚠ <b>${esc(i.name)}:</b>&nbsp;${esc(i.message)}<span class="grow"></span>${i.fix ? `<button data-fix="${esc(i.fix)}">${esc(i.fixLabel || 'Fix')}</button>` : ''}</div>`; });
      $('hBanners').innerHTML = b; const w = $('hWiz'); if (w) w.onclick = () => Wizard.open(true);
      const m = st.music || {}, t = st.tts || {}, ph = st.phone || {}, ch = st.chat || {}, v = st.viewers || {};
      const vRows = plats.filter(p => ch[p] && ch[p].configured).map(p => { const x = (v.platforms || {})[p] || {}; return `<div class="row"><span class="tag ${p}">${P[p].tag}</span><span class="grow">${esc(UI.STATE[x.state] || x.state || '')}</span><b>${x.state === 'live' ? UI.num(x.count) : '–'}</b></div>`; }).join('');
      const pRows = plats.map(p => { const c = ch[p] || {}; return `<div class="row" style="margin:5px 0"><span class="tag ${p}">${P[p].tag}</span><span class="grow small muted">${esc(c.configured ? (c.detail || '') : 'not set up')}</span>${UI.pill(c.configured ? c.state : 'off')}</div>`; }).join('');
      $('hGrid').innerHTML =
        card('🎵 Music', `<div style="font-weight:700;min-height:20px">${esc(m.title || 'Nothing playing')}</div><div class="small muted">${m.playing ? 'Playing' : m.title ? 'Paused' : 'Stopped'} · ${m.queue || 0} in queue${m.ducked ? ' · lowered for the chat voice' : ''}${m.player ? '' : ' · ⚠ player not running in OBS'}</div>
          <div class="actions"><button data-m="prev" aria-label="Previous">⏮</button><button data-m="toggle" class="primary" aria-label="Play or pause">${m.playing ? '⏸ Pause' : '▶ Play'}</button><button data-m="next" aria-label="Next">⏭</button><a class="btn" href="#music">Open music</a></div>`) +
        card('👁 Viewers', `<div class="big">${v.total == null ? '–' : UI.num(v.total)}</div><div class="small muted" style="margin-bottom:8px">${v.total == null ? 'No live numbers right now' : 'total' + (v.partial ? ' (some platforms have no number right now)' : '')}${v.uptime ? ' · live for ' + esc(v.uptime) : ''}</div>${vRows || '<div class="hint">Connect a platform to see viewers.</div>'}`) +
        card('💬 Chat', pRows + `<div class="actions"><a class="btn" href="#accounts">Platforms & accounts</a><a class="btn" href="#chat">Open chat</a></div>`) +
        card('🔊 Chat voice', `<div class="row"><span class="grow">${t.on ? (t.paused ? 'Paused' : t.speaking ? 'Speaking…' : 'On - reading chat') : 'Off'}</span><label class="switch"><input type="checkbox" id="hTts" ${t.on ? 'checked' : ''} aria-label="Chat voice on/off"><i></i></label></div>
          <div class="small muted" style="margin-top:6px">${t.source ? 'Voice source running in OBS' : '⚠ The voice source isn\'t running in OBS'}</div><div class="actions"><button id="hTest">Test voice</button>${t.on ? `<button id="hPause">${t.paused ? '▶ Resume' : '⏸ Pause'}</button>` : ''}<a class="btn" href="#tts">Settings</a></div>`) +
        card('📱 Phone', `<div>${ph.relay === 'not-configured' ? 'Not available in this build' : ph.connected ? '✓ ' + ph.connected + ' phone' + (ph.connected > 1 ? 's' : '') + ' connected' : ph.devices ? ph.devices + ' paired, none connected right now' : 'No phone connected'}</div><div class="small muted">${esc(ph.relay === 'connected' ? 'Works on mobile data and any Wi-Fi' : ph.detail || '')}</div><div class="actions"><a class="btn primary" href="#phone">Connect phone</a></div>`) +
        card('🎥 OBS', `<div class="row"><span class="grow">${st.obs.connected ? 'Connected' : esc(st.obs.detail || 'Not connected')}</span>${UI.pill(st.obs.connected ? 'connected' : st.obs.state === 'off' ? 'off' : 'reconnecting', st.obs.connected ? 'Connected' : st.obs.state === 'off' ? 'Closed' : 'Waiting')}</div><div class="small muted" style="margin-top:6px">${esc(st.obs.setupNote || '')}</div><div class="actions"><button data-fix="obs.setup">Set up OBS again</button><a class="btn" href="#check">System check</a></div>`) +
        card('🤖 Streamer.bot <span class="tiny muted">(optional)</span>', `<div class="row"><span class="grow small muted">${esc(ch.streamerbot ? (ch.streamerbot.detail || (ch.streamerbot.state === 'connected' ? 'Connected - extra actions and commands available' : '')) : '')}</span>${UI.pill(ch.streamerbot ? ch.streamerbot.state : 'off')}</div><div class="hint" style="margin-top:6px">IXC works without Streamer.bot. If you use it, IXC connects to it by itself.</div>`);
      const hT = $('hTts'); if (hT) hT.onchange = () => App.save({ 'tts.on': hT.checked });
      const hP = $('hPause'); if (hP) hP.onclick = () => App.link.send({ type: t.paused ? 'tts.resume' : 'tts.pause' });
      $('hTest').onclick = () => App.link.send({ type: 'tts.test' });
    };
    main.onclick = (e) => { const b = e.target.closest('button'); if (!b) return; if (b.dataset.m) App.link.send({ type: 'music.cmd', c: { cmd: b.dataset.m } }); if (b.dataset.fix) repair(b.dataset.fix, b); };
    paint(); on('status', paint);
  });

  // ======================= CHAT / MUSIC =======================
  page('chat', 'Chat', (main) => { main.style.padding = '0'; main.innerHTML = `<iframe class="frame" src="/chat/chat.html?dock=1" title="Chat" style="height:100%;border:0;border-radius:0"></iframe>`; });
  window.addEventListener('hashchange', () => { $('main').style.padding = ''; });
  page('music', 'Music', (main) => {
    main.innerHTML = `<div class="grid2"><iframe class="frame" src="/music/dock.html" title="Music player" style="height:calc(100vh - 140px);min-height:520px"></iframe><div id="mSet"></div></div>`;
    const paint = () => { $('mSet').innerHTML =
      card('🔉 Lower music while the chat voice speaks', ctl.toggle('music.ducking.enabled', 'Music ducking') + ctl.range('music.ducking.level', 'Music volume while speaking', 'percent of the normal music volume', '%') +
        ctl.range('music.ducking.fadeDownMs', 'Fade down', '', 'ms') + ctl.range('music.ducking.fadeUpMs', 'Fade back up', '', 'ms') + ctl.range('music.ducking.minVolume', 'Never quieter than', 'of the slider volume (0 = can go silent)', '%') +
        `<div class="actions"><button id="duckTest">Test it (plays a test voice)</button></div>`) +
      `<div style="height:14px"></div>` + card('▶ Playback', ctl.toggle('music.autoplay', 'Autoplay', 'when the queue ends, keep playing similar songs') + ctl.toggle('music.repeat', 'Repeat the queue') + ctl.toggle('music.shuffle', 'Shuffle') + ctl.toggle('music.autostart', 'Start music when OBS opens', 'continues where it stopped')) +
      `<div style="height:14px"></div>` + card('📚 Playlists', `<div class="row" style="gap:8px"><input type="text" id="plName" maxlength="60" placeholder="Name, e.g. Chill night" aria-label="Playlist name" style="flex:1"><button id="plSave" class="primary">Save the queue</button></div><div id="plList" class="list" style="margin-top:8px"></div><div class="hint">Play = replaces the queue. Add = adds the songs after the queue. Saved on this PC.</div>`) +
      `<div style="height:14px"></div>` + card('🕘 Recently played', `<div id="hist" class="list small"></div><div class="hint">With Shuffle on, every song in the queue plays once before any song plays again. Autoplay never picks a song that played recently, also not another upload of it.</div>`) +
      `<div style="height:14px"></div>` + card('📁 Your own music files', `<div id="folders" class="list"></div><div class="actions"><button id="addFolder" class="primary">+ Add a music folder</button></div><div class="hint" style="margin-top:8px">MP3, M4A, AAC, OGG, OPUS, WAV and FLAC files in the folder and its subfolders can be searched and played like YouTube songs.</div>`) +
      `<div style="height:14px"></div>` + card('🎚️ Which platform hears the music', `<div class="hint">Switch music and the chat voice on or off per platform in <a href="#audio">Audio routing</a>.</div>`) +
      `<div style="height:14px"></div>` + card('🏷 Now Playing text', ctl.text('nowPlaying.textFormat', 'Text format', '{title} - {artist}', 'use {title}, {artist}, {by}') + ctl.toggle('nowPlaying.writeFile', 'Write nowplaying.txt', 'for OBS Text sources and other tools') +
        `<div class="hint">File: <code>${esc(S.outputFolder)}\\nowplaying.txt</code> · Web: <code>${location.origin}/api/nowplaying.txt</code> · JSON: <code>${location.origin}/api/nowplaying</code></div><div class="actions"><button data-open="output">Open the folder</button></div>`) +
      (S.values['general.advanced'] ? `<div style="height:14px"></div>` + card('🔧 Advanced', ctl.text('music.youtubeApiKey', 'YouTube Data API key', 'optional', 'more reliable search (100 searches/day free)') + ctl.text('music.spotify.clientId', 'Spotify app Client ID', 'optional', 'for Spotify playlists and albums') +
        `<div class="field"><label class="lbl">Spotify Client Secret<div class="hint">stored encrypted, never shown again</div></label><div class="val"><input type="password" id="spSecret" placeholder="paste to set or replace" autocomplete="off"><button id="spSave">Save</button></div></div>` + ctl.text('music.routing.inputName', 'Music source name in OBS', 'IXC Music Player')) : '');
      $('duckTest').onclick = () => App.link.send({ type: 'tts.test' });
      $('addFolder').onclick = async (e) => { e.target.disabled = true; e.target.textContent = 'A folder window opened on your PC…'; const r = await IXC.api('/api/music/folders', {}); e.target.disabled = false; e.target.textContent = '+ Add a music folder'; if (r.ok) { toast('Folder added - scanning…'); App.S.values['music.localFolders'] = r.folders; folders(); } else toast(r.error, true); };
      document.querySelectorAll('[data-open]').forEach(b => b.onclick = () => IXC.api('/api/open', { what: b.dataset.open }));
      const sp = $('spSave'); if (sp) sp.onclick = async () => { const r = await IXC.api('/api/music/spotify-secret', { secret: $('spSecret').value }); $('spSecret').value = ''; toast(r.ok ? 'Spotify secret saved' : r.error, !r.ok); };
      $('plSave').onclick = async () => { const r = await IXC.api('/api/music/playlists', { op: 'save', name: $('plName').value }); toast(r.ok ? 'Saved ' + r.songs + ' songs' : r.error, !r.ok); if (r.ok) { $('plName').value = ''; lists(); } };
      folders(); lists(); };
    async function lists() { const el = $('plList'), hl = $('hist'); if (!el) return;
      const d = await IXC.api('/api/music/playlists'); el.innerHTML = (d.playlists || []).map(p => `<div class="item"><span class="t"><b>${esc(p.name)}</b> <span class="muted">· ${p.songs} songs</span></span><button class="small primary" data-pl="load" data-n="${esc(p.name)}" data-m="replace">▶ Play</button><button class="small" data-pl="load" data-n="${esc(p.name)}" data-m="append">+ Add</button><button class="small" data-pl="delete" data-n="${esc(p.name)}" aria-label="Delete ${esc(p.name)}">🗑</button></div>`).join('') || '<div class="muted small">No playlists yet - fill the queue, type a name and save it.</div>';
      el.querySelectorAll('[data-pl]').forEach(b => b.onclick = async () => { if (b.dataset.pl === 'delete' && !confirm('Delete the playlist "' + b.dataset.n + '"?')) return; const r = await IXC.api('/api/music/playlists', { op: b.dataset.pl, name: b.dataset.n, mode: b.dataset.m, shuffle: !!S.values['music.shuffle'] }); toast(r.ok ? (b.dataset.pl === 'delete' ? 'Deleted' : r.songs + ' songs ' + (b.dataset.m === 'replace' ? 'playing' : 'added')) : r.error, !r.ok); lists(); });
      const h = await IXC.api('/api/music/history'); hl.innerHTML = (h.played || []).slice(0, 15).map(x => `<div class="item"><span class="t">${esc(x.title)}${x.by ? ` <span class="muted">· for ${esc(x.by)}</span>` : ''}</span><span class="muted">${new Date(x.at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span></div>`).join('') || '<div class="muted">Nothing played yet.</div>'; }
    async function folders() { const d = await IXC.api('/api/music/folders'); const el = $('folders'); if (!el) return;
      el.innerHTML = (d.folders || []).map(f => `<div class="item"><span class="t"><b>${esc(f)}</b></span><button class="small" data-rm="${esc(f)}">Remove</button></div>`).join('') + `<div class="hint">${d.songs || 0} songs found${d.scanning ? ' (scanning…)' : ''}</div>`;
      el.querySelectorAll('[data-rm]').forEach(b => b.onclick = () => App.save({ 'music.localFolders': (S.values['music.localFolders'] || []).filter(x => x !== b.dataset.rm) }).then(folders)); }
    paint();
  });

  // ======================= SONG REQUESTS =======================
  page('requests', 'Song requests', (main) => {
    const paint = () => { main.innerHTML = `<div class="grid2"><div>` +
      card('🙋 Song requests from chat', ctl.toggle('songRequests.enabled', 'Allow song requests', 'viewers type <kbd>!sr song name</kbd> or a YouTube link') + ctl.list('songRequests.commands', 'Commands', 'one per line, e.g. !sr', '!sr') +
        ctl.select('songRequests.permission', 'Who can request', App.ROLE_OPTS, 'Followers: Twitch only (signed in)') + ctl.checks('songRequests.platforms', 'From which platforms', App.PLAT_OPTS) +
        ctl.toggle('songRequests.replyInChat', 'Answer in chat', '"@name added …" (needs IXC to be able to reply on that platform)')) +
      `<div style="height:14px"></div>` + card('⏱ Limits', ctl.range('songRequests.maxQueue', 'Max requests waiting') + ctl.range('songRequests.maxPerUser', 'Max per person') + ctl.range('songRequests.userCooldownSec', 'Wait between requests (per person)', '', 's') +
        ctl.range('songRequests.cooldownSec', 'Wait between any two requests', '', 's') + ctl.range('songRequests.maxDurationSec', 'Longest song', 'moderators can go over it', 'mmss') + ctl.range('songRequests.minAccountAgeDays', 'Minimum account age', 'Twitch only', 'd') +
        ctl.toggle('songRequests.allowLinks', 'Allow links') + ctl.toggle('songRequests.allowSearch', 'Allow song names (IXC searches YouTube)')) +
      `<div style="height:14px"></div>` + card('🚫 Not allowed', ctl.list('songRequests.bannedSongs', 'Banned songs', 'words in the title or a video ID', 'one per line') + ctl.list('songRequests.bannedArtists', 'Banned artists', '', 'one per line')) +
      `</div><div>` + card('📋 Waiting requests', `<div id="rq" class="list"></div>`) + `<div style="height:14px"></div>` +
      card('ℹ️ Chat commands for requests', `<div class="kv"><span><kbd>!sr name or link</kbd></span><span>request a song</span><span><kbd>!wrongsong</kbd></span><span>remove your last request</span><span><kbd>!np</kbd></span><span>what's playing</span><span><kbd>!queue</kbd></span><span>what's next</span><span><kbd>!skip</kbd></span><span>moderators: skip the song</span></div><div class="hint" style="margin-top:8px">Change these under Commands.</div>`) + `</div></div>`;
      list(); };
    function list() { const el = $('rq'); if (!el) return; const m = S.music || {}; const q = (m.queue || []).map((x, i) => [x, i]).filter(([x, i]) => i > (m.index ?? -1) && x.by);
      el.innerHTML = q.length ? q.map(([x, i]) => `<div class="item"><img class="th" src="${UI.thumb(x)}" alt=""><div class="t"><b>${esc(x.title || x.q)}</b><small>${esc(x.by)} on ${esc(P[x.platform] ? P[x.platform].name : x.platform || '')}${x.durationSec ? ' · ' + UI.time(x.durationSec) : ''}</small></div><button class="small" data-j="${i}" title="Play now">▶</button><button class="small" data-x="${x.uid}" title="Remove">✕</button></div>`).join('') : '<div class="hint">No requests waiting.</div>';
      el.querySelectorAll('[data-j]').forEach(b => b.onclick = () => App.link.send({ type: 'music.cmd', c: { cmd: 'jump', i: +b.dataset.j } }));
      el.querySelectorAll('[data-x]').forEach(b => b.onclick = () => App.link.send({ type: 'music.cmd', c: { cmd: 'remove', uid: b.dataset.x } })); }
    paint(); on('music', list);
  });

  // ======================= PLATFORMS & ACCOUNTS =======================
  page('accounts', 'Platforms & Accounts', (main) => {
    main.innerHTML = `<p class="muted" style="margin-top:0">Reading chat needs only your channel name. Replies, alerts and your platform sign-ins are handled by <b>Streamer.bot</b> (connect Twitch, YouTube and Kick there, under Platforms). No developer apps needed.</p><div class="grid" id="acc"></div><div class="grid" id="appsBox" style="margin-top:12px"></div>`;
    const NOTE = { twitch: 'Reads chat without signing in. Sign in for replies, follower alerts and viewer counts.', kick: 'Reads chat and viewers without signing in. Sign in to reply.', youtube: 'Finds your live stream by itself. Sign in to reply in YouTube chat.', rumble: 'Copy your "Live Stream API" link from rumble.com (Account > Live Stream API) and paste it here. Rumble doesn\'t allow apps to send chat.' };
    const paint = () => { const st = (S.status || {}).chat || {}, acc = S.accounts || {};
      $('acc').innerHTML = plats.map(p => { const s = st[p] || {}, a = acc[p] || {}, ac = a.account, fl = a.flow;
        const chan = p === 'rumble' ? `<div class="field"><label class="lbl">Live Stream API link</label><div class="val"><input type="url" id="rumbleUrl" placeholder="${ac ? '(saved - paste a new one to replace it)' : 'https://rumble.com/-livestream-api/get-data?key=…'}"><button id="rumbleSave">Save</button></div></div><div class="actions"><button data-open-url="https://rumble.com/account/livestream-api">Open rumble.com to copy it</button></div>`
          : ctl.text('platforms.' + p + '.channel', p === 'youtube' ? 'Your channel' : 'Your channel name', p === 'youtube' ? '@yourhandle or channel link' : p === 'twitch' ? 'yourname' : 'your-name');
        const account = p === 'rumble' ? '' : !a.available ? (S.values['general.advanced'] ? `<div class="hint">Replies go through Streamer.bot. Advanced: add your own ${P[p].name} app under <b>Your apps</b> below.</div>` : '') : ac ? `<div class="row" style="margin-top:6px"><span class="grow">Signed in as <b>${esc(ac.displayName || ac.login)}</b>${ac.invalid ? ' <span class="warn">(expired)</span>' : ''}</span>${ac.invalid ? `<button class="primary" data-connect="${p}">Sign in again</button>` : ''}<button data-signout="${p}">Sign out</button></div>`
          : fl && fl.state === 'waiting' ? `<div class="banner info" style="margin-top:6px">${esc(fl.message)}${fl.userCode ? ` - code <b style="font-size:18px;letter-spacing:2px">${esc(fl.userCode)}</b>` : ''}<span class="grow"></span>${fl.url ? `<button data-open-url="${esc(fl.url)}">Open again</button>` : ''}<button data-cancel="${p}">Cancel</button></div>`
          : `<div class="actions"><button class="primary" data-connect="${p}">Connect ${P[p].name} account</button></div>${fl && fl.state === 'failed' ? `<div class="err small">${esc(fl.message)}</div>` : ''}`;
        return `<div class="card"><div class="plat"><span class="logo2 ${p}">${P[p].tag.slice(0, 2)}</span><div class="grow"><h3 style="margin:0">${P[p].name}</h3><div class="small muted">${esc(s.configured ? s.detail || '' : '')}</div></div>${UI.pill(s.configured ? s.state : 'off')}</div>${s.configured ? `<div class="tiny muted" style="margin:6px 0 2px">Last chat message: ${s.lastMessage ? new Date(s.lastMessage).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : 'none since IXC started'} · ${ac ? (ac.invalid ? '⚠ sign-in expired - connect again' : 'can reply') : p === 'rumble' ? 'read-only (Rumble has no sending API)' : 'read-only until you connect the account'}</div>` : ''}
          ${ctl.toggle('platforms.' + p + '.enabled', 'Use ' + P[p].name)}${chan}${account}<div class="hint" style="margin-top:8px">${NOTE[p]}${s.configured && s.sendVia === 'streamerbot' ? '<br>Replies: sent through Streamer.bot ✓' : s.configured && !s.canSend && p !== 'rumble' ? '<br>Replies: ' + esc(s.sendNote) : ''}</div></div>`; }).join('') +
        `<div class="card"><div class="plat"><span class="logo2 streamerbot">SB</span><div class="grow"><h3 style="margin:0">Streamer.bot <span class="tiny muted">(replies, alerts and platform sign-ins)</span></h3><div class="small muted">${esc((st.streamerbot || {}).detail || '')}</div></div>${UI.pill((st.streamerbot || {}).state || 'off')}</div>
          ${ctl.select('platforms.streamerbot.mode', 'Use Streamer.bot', [['auto', 'Automatically, when it\'s running'], ['on', 'Always (keep trying)'], ['off', 'Never']])}
          <div class="actions"><button data-go="streamerbot">Streamer.bot settings &amp; status</button></div><div class="hint">Connect your platforms inside Streamer.bot; IXC reads their chat and events and sends your replies through it. IXC's own commands, timers, chat voice, music and OBS controls keep working without it.</div>${S.values['general.advanced'] ? ctl.text('platforms.streamerbot.websocketUrl', 'WebSocket address', 'ws://127.0.0.1:8080/') : ''}</div>`;
      paintApps();
      const rs = $('rumbleSave'); if (rs) rs.onclick = async () => { const r = await IXC.api('/api/accounts/rumble/rumble', { url: $('rumbleUrl').value }); toast(r.ok ? 'Rumble connected' : r.error, !r.ok); }; };
    // your own developer apps: replies, alerts and viewers without Streamer.bot or a relay. Secrets are never shown back.
    const APPS = [
      ['twitch', 'Twitch', 'dev.twitch.tv/console/apps', ['Click <b>Register Your Application</b>.', 'Name: anything (e.g. <i>IXC yourname</i>). OAuth Redirect URL: <code>http://localhost</code> (not used). Category: <b>Broadcaster Suite</b>. Client Type: <b>Public</b>.', 'Click <b>Create</b>, then <b>Manage</b>, and copy the <b>Client ID</b> here. Public apps have no secret.']],
      ['google', 'YouTube (Google)', 'console.cloud.google.com/apis/library/youtube.googleapis.com', ['Create a project and click <b>Enable</b> for <b>YouTube Data API v3</b>.', '<b>OAuth consent screen</b>: External, add yourself under <b>Test users</b>, then click <b>Publish app</b> (an app left in Testing loses its sign-in every 7 days).', '<b>Credentials > Create credentials > OAuth client ID</b>, type <b>Desktop app</b>. Copy the <b>Client ID</b> and <b>Client secret</b> here.']],
      ['kick', 'Kick', 'kick.com/settings/developer', ['Click <b>Create new app</b>.', 'Redirect URL: <code>{redirect}</code> (exactly this). Scopes: <b>Read user information</b>, <b>Read channel information</b>, <b>Write to Chat feed</b>, <b>Moderate</b>.', 'Copy the <b>Client ID</b> and <b>Client Secret</b> here.']]];
    function appsCard(apps) {
      const anyMissing = APPS.some(([k]) => !(apps[k] || {}).available);
      return `<div class="card" style="grid-column:1/-1"><h3>🔑 Your apps <span class="tiny muted">(for replies, alerts and viewer counts)</span></h3>
        <div class="hint">To send messages, Twitch, YouTube and Kick need a free developer app that belongs to you. Create it once (about 5 minutes each), paste the IDs here, then press <b>Connect</b> above. Secrets are stored encrypted on this PC only and are never shown again. ${anyMissing ? '' : '<b>All set ✓</b>'}</div>
        ${APPS.map(([k, name, url, steps]) => { const a = apps[k] || {};
          return `<details data-k="${k}" style="margin-top:8px" ${a.available ? '' : 'open'}><summary><b>${name}</b> ${a.available ? '<span class="ok small">✓ ready</span>' : '<span class="muted small">not set up</span>'}${a.own ? '' : a.builtIn ? ' <span class="tiny muted">(using the built-in app)</span>' : ''}</summary>
            <ol class="small" style="margin:6px 0 8px 18px;padding:0"><li>Open <a href="#" data-open-url="https://${url}">${url}</a> and sign in.</li>${steps.map(x => `<li>${x.replace('{redirect}', esc(a.redirect || ''))}</li>`).join('')}</ol>
            <div class="field"><label class="lbl">Client ID</label><div class="val"><input id="app_${k}_id" value="${esc(a.id || '')}" autocomplete="off" spellcheck="false"></div></div>
            ${k === 'twitch' ? '' : `<div class="field"><label class="lbl">Client secret</label><div class="val"><input id="app_${k}_secret" type="password" autocomplete="new-password" placeholder="${a.secretSet ? '(saved - paste a new one to replace it)' : ''}">${a.secretSet ? ' <span class="ok small" id="app_' + k + '_saved">🔒 Secret saved</span>' : ''}</div></div>`}
            <div class="actions"><button class="primary" data-app-save="${k}">Save</button>${a.own ? `<button data-app-clear="${k}">Remove</button>` : ''}</div><div class="small" id="app_${k}_msg" role="status" style="margin-top:6px"></div></details>`; }).join('')}</div>`; }
    // The card is redrawn only when the saved app data changed. Whatever is typed but not saved yet (IDs, secrets, which steps are open) is kept.
    let appsKey = null;
    function paintApps(force, resetKey) { const box = $('appsBox'); if (!box) return; const apps = (S.accounts || {}).apps || {}, key = JSON.stringify(apps) + !!S.values['general.advanced'];
      // developer apps are an advanced alternative to Streamer.bot: shown only in Advanced mode or when one is already saved
      if (!S.values['general.advanced'] && !Object.values(apps).some(a => a && a.own)) { box.innerHTML = ''; appsKey = key; return; }
      if (!force && key === appsKey && box.firstChild) return; appsKey = key;
      const draft = {}, open = {}; box.querySelectorAll('input').forEach(i => { draft[i.id] = i.value; }); box.querySelectorAll('details').forEach(d => { open[d.dataset.k] = d.open; });
      box.innerHTML = appsCard(apps);
      box.querySelectorAll('input').forEach(i => { const k = i.id.split('_')[1]; if (k !== resetKey && draft[i.id] && draft[i.id] !== i.value) i.value = draft[i.id]; });
      box.querySelectorAll('details').forEach(d => { if (d.dataset.k === resetKey) d.open = true; else if (open[d.dataset.k] !== undefined) d.open = open[d.dataset.k]; }); }
    function appMsg(k, text, bad) { const m = $('app_' + k + '_msg'); if (m) { m.className = 'small ' + (bad ? 'err' : 'ok'); m.textContent = text; } }
    main.onclick = async (e) => { const b = e.target.closest('button,a[data-open-url]'); if (!b) return; const d = b.dataset;
      if (d.appSave) { const k = d.appSave, idv = $('app_' + k + '_id').value.trim(), se = $('app_' + k + '_secret'), sv = se ? se.value.trim() : '', body = { [k]: {} };
        // a blank field means "leave it alone" (Remove is the way to clear), so an empty box can never wipe a saved ID
        if (idv) body[k].id = idv; if (sv) body[k].secret = sv;
        if (!idv && !sv) { appMsg(k, 'Nothing to save yet - paste the Client ID' + (k === 'twitch' ? '' : ' and the Client secret') + ' first.', true); return; }
        b.disabled = true; const r = await IXC.api('/api/accounts/apps', body); b.disabled = false;
        if (!r || !r.ok) { const why = (r && r.error) || 'IXC did not answer - is it running?'; appMsg(k, 'Not saved: ' + why + ' Your entries are still here.', true); toast('Not saved: ' + why, true); return; }   // fields are left exactly as typed
        // confirm from the backend, not from what the browser remembers
        const fresh = await IXC.api('/api/accounts'); const ap = fresh && fresh.apps && fresh.apps[k];
        if (!ap || (idv && ap.id !== idv) || (sv && !ap.secretSet)) { appMsg(k, 'IXC said "saved" but reading the settings back did not show them. Your entries are still here - try again.', true); toast('Could not confirm the save', true); return; }
        S.accounts = fresh; paintApps(true, k); appMsg(k, 'Saved' + (sv ? ' - the secret is stored encrypted and is never shown again' : '') + '. Now press Connect above.', false); toast('Saved - now press Connect above'); return; }
      if (d.appClear && await UI.confirmBox('Remove your app settings for this platform? You stay signed in until the sign-in expires.', 'Remove')) { const r = await IXC.api('/api/accounts/apps', { [d.appClear]: { id: '', secret: '' } }); if (r && r.ok) { S.accounts = await IXC.api('/api/accounts'); paintApps(true, d.appClear); paint(); } else appMsg(d.appClear, 'Not removed: ' + ((r && r.error) || 'IXC did not answer'), true); return; }
      if (d.go) { location.hash = '#' + d.go; return; }
      if (d.openUrl) { e.preventDefault(); window.open(d.openUrl, '_blank'); return; }
      if (d.connect) { b.disabled = true; const r = await IXC.api('/api/accounts/' + d.connect + '/connect', {}); b.disabled = false; if (!r.ok) toast(r.error, true); else toast('Your browser opened - finish signing in there'); }
      if (d.signout && await UI.confirmBox('Sign out of ' + P[d.signout].name + '? IXC keeps reading chat but can\'t reply there.', 'Sign out')) await IXC.api('/api/accounts/' + d.signout + '/signout', {});
      if (d.cancel) await IXC.api('/api/accounts/' + d.cancel + '/cancel', {});
      if (d.openUrl) window.open(d.openUrl, '_blank'); };
    paint(); on('status', paint); on('accounts', paint);
  });

  // ======================= OVERLAYS =======================
  const OV = {
    nowplaying: { name: 'Now Playing', w: 600, h: 140, fields: [['layout', 'Layout', [['full', 'Full (art, title, artist, progress)'], ['compact', 'Compact (one line)'], ['minimal', 'Minimal (🎵 Title — Artist)']]], ['label', 'Heading', 'text'], ['showArt', 'Show artwork'], ['showArtist', 'Show artist'], ['showProgress', 'Progress bar'], ['time', 'Time', [['elapsed', '2:14 / 4:02'], ['remaining', 'Time left'], ['none', 'Hide']]], ['showRequester', 'Show who requested it'], ['hideWhenPaused', 'Hide when nothing plays']] },
    queue: { name: 'Song queue', w: 500, h: 260, fields: [['title', 'Heading', 'text'], ['max', 'Songs shown', 'num', 1, 15], ['showRequester', 'Show who requested'], ['requestsOnly', 'Only chat requests'], ['hideWhenEmpty', 'Hide when empty']] },
    chat: { name: 'Chat', w: 500, h: 600, fields: [['max', 'Messages on screen', 'num', 1, 30], ['fade', 'Fade out after (seconds, 0 = never)', 'num', 0, 600], ['platforms', 'Platforms', 'plats'], ['showAvatars', 'Avatars'], ['showPlatform', 'Platform tags'], ['showRoles', 'Mod / VIP badges'], ['showAlerts', 'Show follows / subs in chat']] },
    viewers: { name: 'Viewer counter', w: 700, h: 80, fields: [['layout', 'Layout', [['row', 'In a row'], ['column', 'Stacked'], ['total', 'Total only']]], ['platforms', 'Platforms', 'plats'], ['showNames', 'Platform names'], ['showTotal', 'Show total'], ['totalLabel', 'Total label', 'text'], ['hideOffline', 'Hide offline platforms']] },
    alerts: { name: 'Alerts', w: 600, h: 300, fields: [['kinds', 'Show', 'kinds'], ['platforms', 'Platforms', 'plats'], ['seconds', 'Seconds on screen', 'num', 2, 60], ['max', 'At most on screen', 'num', 1, 6], ['layout', 'Layout', [['stack', 'Stack'], ['latest', 'Latest only']]]] },
    tts: { name: 'Chat voice indicator', w: 700, h: 80, fields: [['showName', 'Show name'], ['showText', 'Show the text being read']] },
    status: { name: 'Live status badge', w: 700, h: 70, fields: [['label', 'Label', 'text'], ['showPlatforms', 'Platforms you\'re live on'], ['showViewers', 'Total viewers'], ['showMusic', 'Song playing'], ['showTts', 'Chat voice speaking']] },
  };
  const COMMON = [['font', 'Font', 'text'], ['size', 'Text size', 'num', 10, 80], ['color', 'Text color', 'color'], ['accent', 'Accent color', 'color'], ['bg', 'Background', 'color'], ['bgOpacity', 'Background opacity %', 'num', 0, 100], ['radius', 'Corner roundness', 'num', 0, 40], ['align', 'Align', [['left', 'Left'], ['center', 'Center'], ['right', 'Right']]], ['animation', 'Animation', [['slide', 'Slide'], ['fade', 'Fade'], ['pop', 'Pop'], ['none', 'None']]]];
  page('overlays', 'Overlays', (main, qs) => {
    const sel = qs.get('o') || 'nowplaying';
    main.innerHTML = `<p class="muted" style="margin-top:0">Pick an overlay, style it, then press <b>Add to OBS</b> - IXC puts it into the scene that's on air. Changes apply live, even after it's in OBS.</p>
      <div class="row wrap section">${Object.entries(OV).map(([k, o]) => `<a class="btn ${k === sel ? 'primary' : ''}" href="#overlays?o=${k}">${esc(o.name)}</a>`).join('')}</div>
      <div class="grid2"><div class="card" id="ovForm"></div><div><div class="preview" style="height:${Math.min(460, OV[sel].h + 60)}px;overflow:hidden"><iframe id="ovPrev" src="/overlay/${sel}.html?preview=1" title="Preview" style="width:100%;height:100%;border:0;background:transparent;color-scheme:normal" allowtransparency="true"></iframe></div>
      <div class="card" style="margin-top:14px"><h3>Use it in OBS</h3><div class="row"><input type="text" readonly id="ovUrl" value="${location.origin}/overlay/${sel}.html" aria-label="Overlay address"><button id="ovCopy">Copy</button></div>
      <div class="actions"><button class="primary" id="ovAdd">➕ Add to OBS (current scene)</button><button id="ovReset">Reset style</button></div><div class="hint" style="margin-top:8px">Size in OBS: ${OV[sel].w}×${OV[sel].h} (drag the edges in OBS to resize). Text files for OBS "Text" sources: <code>${esc(S.outputFolder)}</code></div></div></div></div>`;
    const o = OV[sel]; let cur = Object.assign({}, S.values['overlays.' + sel] || {});
    const input = ([k, label, kind, a, b]) => { const v = cur[k];
      if (Array.isArray(kind)) return `<div class="field"><label class="lbl">${esc(label)}</label><div class="val"><select data-o="${k}">${kind.map(([x, t]) => `<option value="${x}" ${v === x ? 'selected' : ''}>${esc(t)}</option>`).join('')}<option value="" ${v == null ? 'selected' : ''}>(default)</option></select></div></div>`;
      if (kind === 'text') return `<div class="field"><label class="lbl">${esc(label)}</label><div class="val"><input type="text" data-o="${k}" value="${esc(v ?? '')}" placeholder="(default)"></div></div>`;
      if (kind === 'num') return `<div class="field"><label class="lbl">${esc(label)}</label><div class="val"><input type="number" data-o="${k}" min="${a}" max="${b}" value="${v ?? ''}" placeholder="(default)"></div></div>`;
      if (kind === 'color') return `<div class="field"><label class="lbl">${esc(label)}</label><div class="val"><input type="color" data-o="${k}" value="${/^#[0-9a-f]{6}$/i.test(v || '') ? v : '#ffffff'}" style="width:60px;height:34px;padding:2px"><button class="small ghost" data-unset="${k}">default</button></div></div>`;
      if (kind === 'plats' || kind === 'kinds') { const opts = kind === 'plats' ? App.PLAT_OPTS : [['follow', 'Follows'], ['sub', 'Subs'], ['gift', 'Gift subs'], ['raid', 'Raids'], ['superchat', 'Super Chats / Rants'], ['member', 'Members']]; const vs = Array.isArray(v) ? v : opts.map(x => x[0]);
        return `<div class="field"><label class="lbl">${esc(label)}</label><div class="val chipset" data-ol="${k}">${opts.map(([x, t]) => `<label><input type="checkbox" value="${x}" ${vs.includes(x) ? 'checked' : ''}>${esc(t)}</label>`).join('')}</div></div>`; }
      return `<div class="field"><label class="lbl">${esc(label)}</label><div class="val"><select data-o="${k}" data-bool="1"><option value="" ${v == null ? 'selected' : ''}>(default)</option><option value="1" ${v === true ? 'selected' : ''}>Yes</option><option value="0" ${v === false ? 'selected' : ''}>No</option></select></div></div>`; };
    $('ovForm').innerHTML = `<h3>${esc(o.name)}</h3>` + o.fields.map(input).join('') + '<h3 style="margin-top:16px">Look</h3>' + COMMON.map(input).join('');
    let t; const push = () => { clearTimeout(t); t = setTimeout(async () => { const r = await IXC.api('/api/settings', { patch: { ['overlays.' + sel]: cur } }); if (!r.ok) toast(r.error, true); else S.values = r.values; }, 250); };
    $('ovForm').addEventListener('input', (e) => { const el = e.target; const k = el.dataset.o; const lk = el.closest('[data-ol]');
      if (lk) cur[lk.dataset.ol] = [...lk.querySelectorAll('input:checked')].map(x => x.value);
      else if (k) { if (el.value === '') delete cur[k]; else cur[k] = el.dataset.bool ? el.value === '1' : el.type === 'number' ? +el.value : el.value; }
      push(); });
    $('ovForm').addEventListener('click', (e) => { const u = e.target.dataset.unset; if (u) { delete cur[u]; push(); } });
    $('ovCopy').onclick = async () => { try { await navigator.clipboard.writeText($('ovUrl').value); toast('Copied'); } catch { $('ovUrl').select(); } };
    $('ovReset').onclick = async () => { cur = {}; await IXC.api('/api/settings', { patch: { ['overlays.' + sel]: {} } }); App.route(); };
    $('ovAdd').onclick = async (e) => { e.target.disabled = true; const r = await IXC.api('/api/obs/overlay', { name: o.name, path: '/overlay/' + sel + '.html', width: o.w, height: o.h }); e.target.disabled = false; toast(r.ok ? 'Added "' + r.source + '" to the scene "' + r.scene + '"' : (r.error || 'OBS is not connected'), !r.ok); };
  });

  // ======================= PHONE =======================
  page('phone', 'Phone remote', (main) => {
    main.innerHTML = `<div class="grid2"><div class="card"><h3>📱 Connect a phone</h3><p class="muted" style="margin-top:0">Control music, chat voice and chat from your phone - on mobile data or any Wi-Fi. No app, no account, no router or firewall setup.</p>
      <div id="phState"></div><div class="actions"><button class="primary" id="phPair">Show QR code</button><button id="phQuick" style="display:none">Use Quick connect instead</button><button id="phStop" style="display:none">Stop Quick connect</button></div>
      <div id="phQr" style="display:none;text-align:center;margin-top:16px"><div class="qr" id="qrBox"></div><div id="phWait" style="margin-top:10px"></div><div class="hint">Scan with the phone's camera. The code works once and expires in <b id="phLeft">5:00</b>. Only scan it yourself - whoever scans it can control IXC.</div><div class="actions" style="justify-content:center"><button id="phNew">New code</button><button id="phCancel">Close</button></div></div></div>
      <div class="card"><h3>Paired phones</h3><div id="phList" class="list"></div><div class="actions"><button id="phAll">Remove all phones</button></div></div></div>`;
    let timer, want = null;   // want: the QR the user asked for, shown as soon as Quick connect is ready
    const relayOk = () => { const r = S.remote || {}; return r.configured && r.relay === 'connected'; };
    const paint = () => { const r = S.remote || {}, q = r.quick || {};
      let st;
      if (r.enabled === false) st = '<div class="banner">The phone remote is turned off (Settings).</div>';
      else if (relayOk()) st = '<div class="banner ok">✓ Ready - your phone stays paired (permanent link)</div>';
      else if (q.state === 'online') st = '<div class="banner ok">✓ Quick connect is on - works on mobile data and any Wi-Fi. It\'s a temporary link: it closes when IXC closes or after 30 minutes without a phone, then you just scan a new code.</div>';
      else if (q.state === 'starting' || q.state === 'downloading') st = `<div class="banner info"><span class="spin"></span> ${esc(q.message || 'Starting Quick connect…')}</div>`;
      else if (q.state === 'error') st = `<div class="banner err">${esc(q.message)}</div>`;
      else st = '<div class="hint">Press <b>Show QR code</b> and scan it with your phone\'s camera. That\'s all.</div>';
      $('phState').innerHTML = st; $('phPair').disabled = r.enabled === false || q.state === 'starting' || q.state === 'downloading';
      $('phQuick').style.display = relayOk() ? '' : 'none'; $('phStop').style.display = q.state === 'online' ? '' : 'none';
      $('phList').innerHTML = (r.devices || []).length ? r.devices.map(d => `<div class="item"><span class="dot" style="background:${d.online ? 'var(--ok)' : 'var(--dim2)'}"></span><div class="t"><b>${esc(d.name)}</b><small>${d.online ? 'Connected · ' + esc(d.connection) + (d.latencyMs ? ' · ' + d.latencyMs + ' ms' : '') : 'Last seen ' + esc((d.lastSeen || '').replace('T', ' ').slice(0, 16))}${d.temp ? ' · until Quick connect closes' : ''}</small></div>
        <button class="small" data-rn="${d.id}">Rename</button>${d.online ? `<button class="small" data-dc="${d.id}">Disconnect</button>` : ''}<button class="small" data-rm="${d.id}">Remove</button></div>`).join('') : '<div class="hint">No phones yet.</div>';
      if (want && q.state === 'online') pair(want); else if (want && q.state === 'error') want = null; };
    async function pair(mode) { want = null; const b = $('phPair'); b.disabled = true; const r = await IXC.api('/api/remote/pair', { mode: mode || '' }); b.disabled = false;
      if (r.starting) { want = 'quick'; S.remote = Object.assign({}, S.remote, { quick: Object.assign({}, (S.remote || {}).quick, { state: r.state, message: r.error }) }); paint(); return; }   // the QR appears by itself when the link is ready
      if (!r.ok) { toast(r.error, true); return; } $('phQr').style.display = ''; UI.qr($('qrBox'), r.url); $('phWait').innerHTML = '<span class="spin"></span> Waiting for the phone…';
      let left = r.expiresIn || 300; clearInterval(timer); timer = setInterval(() => { if (!$('phLeft')) return clearInterval(timer); left--; $('phLeft').textContent = Math.floor(left / 60) + ':' + String(left % 60).padStart(2, '0'); if (left <= 0) { clearInterval(timer); $('qrBox').innerHTML = ''; $('phWait').textContent = 'Code expired - press New code.'; } }, 1000); }
    $('phPair').onclick = () => pair(''); $('phQuick').onclick = () => pair('quick'); $('phNew').onclick = () => pair(''); $('phCancel').onclick = async () => { want = null; clearInterval(timer); $('phQr').style.display = 'none'; await IXC.api('/api/remote/cancel', {}); };
    $('phStop').onclick = async () => { clearInterval(timer); $('phQr').style.display = 'none'; await IXC.api('/api/remote/quick/stop', {}); };
    $('phAll').onclick = async () => { if (await UI.confirmBox('Remove all paired phones? They will need a new QR code.', 'Remove all')) await IXC.api('/api/remote/revoke', {}); };
    $('phList').onclick = async (e) => { const d = e.target.dataset;
      if (d.rn) { const n = prompt('New name for this phone:'); if (n) { const r = await IXC.api('/api/remote/rename', { id: d.rn, name: n }); if (!r.ok) toast(r.error, true); } }
      if (d.dc) await IXC.api('/api/remote/disconnect', { id: d.dc });
      if (d.rm && await UI.confirmBox('Remove this phone? It will need a new QR code.', 'Remove')) await IXC.api('/api/remote/revoke', { id: d.rm }); };
    on('paired', (m) => { clearInterval(timer); $('qrBox').innerHTML = ''; $('phWait').innerHTML = `<div class="banner ok">✓ Phone connected: <b>${esc(m.name)}</b></div>`; toast('Phone connected'); });
    paint(); on('remote', paint); IXC.api('/api/remote/status').then(r => { if (r.type) { S.remote = r; paint(); } });
  });

  // ======================= CHAT VOICE =======================
  page('tts', 'Chat voice (TTS)', (main) => {
    const vopts = () => S.voices.map(v => [v.key, v.label + (v.isNew ? ' ★' : '')]);
    const paint = () => { main.innerHTML = `<div class="grid2"><div>` +
      card('🔊 Chat voice', ctl.toggle('tts.on', 'Read chat out loud', 'viewers hear it through the IXC TTS source in OBS') + ctl.toggle('tts.paused', 'Paused', 'keeps messages waiting until you resume') + ctl.select('tts.voice', 'Default voice', vopts()) +
        ctl.select('tts.readMode', 'What to read', [['name', 'Name + message ("Alex says hi")'], ['all', 'Only the message'], ['tts', 'Only messages starting with !tts']]) + ctl.range('tts.speed', 'Speed', '', 'x') + ctl.range('tts.pitch', 'Pitch') + ctl.range('tts.volume', 'Volume', '', '%') +
        ctl.toggle('tts.fallback', 'Offline backup voice', 'if the online voice fails, use the Windows voice') + `<div class="actions"><button id="tTest">▶ Test voice</button><button id="tSkip">Skip current</button><button id="tClear">Clear waiting</button></div><div class="hint" id="tEng"></div>`) +
      `<div style="height:14px"></div>` + card('🎯 What gets read', ctl.checks('tts.platforms', 'Platforms', App.PLAT_OPTS) + ctl.select('tts.minRole', 'Who', App.ROLE_OPTS) + ctl.toggle('tts.ignoreOwn', 'Skip my own messages') + ctl.toggle('tts.ignoreBots', 'Skip bots (Nightbot, StreamElements…)') +
        ctl.toggle('tts.readEmotes', 'Read emote names') + ctl.toggle('tts.readEmoji', 'Read emoji') + ctl.toggle('tts.readLinks', 'Read links out (otherwise "link")') + ctl.range('tts.maxChars', 'Longest message', 'characters') + ctl.toggle('tts.modControls', 'Moderators can control it', '!ttsskip, !ttson, !ttsoff')) +
      `<div style="height:14px"></div>` + card('🚦 Busy chat', ctl.range('tts.queueMax', 'Messages waiting at most') + ctl.range('tts.staleSec', 'Skip messages older than', '', 's') + ctl.range('tts.perMinute', 'At most per minute') + ctl.range('tts.userCooldownSec', 'Same person: wait', '', 's')) +
      `<div style="height:14px"></div>` + card('📝 Lists', ctl.list('tts.neverSpeak', 'Never read these people') + ctl.list('tts.blockedWords', 'Skip messages containing') + ctl.list('tts.botNames', 'More bot names') + ctl.list('tts.ownNames', 'My other account names', 'IXC finds your connected accounts by itself')) +
      `</div><div>` + card('🗣 Now', `<div id="tNow"></div>`) + `<div style="height:14px"></div>` + card('⏳ Waiting', `<div id="tQ" class="list"></div>`) + `<div style="height:14px"></div>` + card('✅ Recently read', `<div id="tH" class="list"></div>`) + `<div style="height:14px"></div>` + card('⏭ Not read (and why)', `<div id="tS" class="list"></div>`) + `</div></div>`;
      $('tTest').onclick = () => App.link.send({ type: 'tts.test' }); $('tSkip').onclick = () => App.link.send({ type: 'tts.skip' }); $('tClear').onclick = () => App.link.send({ type: 'tts.clear' });
      live(); };
    function live() { const t = S.tts; if (!t || !$('tQ')) return; const row = (h, extra) => `<div class="item"><span class="tag ${esc(h.platform)}">${esc(P[h.platform] ? P[h.platform].tag : 'TEST')}</span><div class="t"><b>${esc(h.text)}</b><small>${esc(h.user || '')} · ${esc(h.status)}${h.engine ? ' · ' + esc(h.engine) : ''}</small></div>${extra || ''}</div>`;
      $('tNow').innerHTML = t.current ? row(t.current) : `<div class="hint">${t.paused ? 'Paused.' : 'Nothing being read.'} ${t.players ? '' : '⚠ The voice source isn\'t running in OBS.'}</div>`;
      $('tQ').innerHTML = (t.queue || []).map(h => row(h)).join('') || '<div class="hint">Nothing waiting.</div>';
      $('tH').innerHTML = (t.history || []).map(h => row(h, `<button class="small" data-rp="${h.n}" title="Say it again">↻</button>`)).join('') || '<div class="hint">Nothing read yet.</div>';
      $('tS').innerHTML = (t.skipped || []).map(h => `<div class="item"><div class="t"><b>${esc(h.user ? h.user + ': ' : '')}${esc(h.text)}</b><small>${esc(h.at)} · ${esc(h.reason)}</small></div></div>`).join('') || '<div class="hint">Nothing skipped.</div>';
      $('tEng').textContent = 'Voice engine: ' + (t.engine || ''); $('tH').onclick = (e) => { const n = e.target.dataset.rp; if (n) App.link.send({ type: 'tts.replay', n: +n }); }; }
    paint(); on('tts', live);
  });
  page('voices', 'Voices for people', (main, qs) => {
    const vopts = (sel) => `<option value="">(default voice)</option>` + S.voices.map(v => `<option value="${esc(v.key)}" ${v.key === sel ? 'selected' : ''}>${esc(v.label)}</option>`).join('');
    const row = (scope, key, v, label) => `<tr data-scope="${scope}" data-key="${esc(key)}"><td>${label}</td><td><select data-f="voice">${vopts(v.voice)}</select></td><td><input type="number" data-f="speed" min="-50" max="100" value="${v.speed ?? ''}" placeholder="0" style="width:80px" title="Speed % (+10 = 10% faster)"></td>
      <td><input type="number" data-f="pitch" min="-30" max="30" value="${v.pitch ?? ''}" placeholder="0" style="width:70px"></td><td><input type="number" data-f="volume" min="0" max="100" value="${v.volume ?? ''}" placeholder="100" style="width:70px"></td>
      <td><label class="switch"><input type="checkbox" data-f="enabled" ${v.enabled !== false ? 'checked' : ''} aria-label="Read this person"><i></i></label></td><td><button class="small" data-test="1">▶</button> ${scope === 'user' ? '<button class="small" data-del="1">✕</button>' : ''}</td></tr>`;
    const head = '<tr><th>Who</th><th>Voice</th><th>Speed %</th><th>Pitch</th><th>Volume %</th><th>Read</th><th></th></tr>';
    const paint = () => { const users = S.values['tts.users'] || {}, roles = S.values['tts.roleVoices'] || {}, pv = S.values['tts.platformVoices'] || {};
      main.innerHTML = card('🎙 Voices for people', `<p class="muted" style="margin-top:0">Give regulars their own voice. Order: the person → their role → their platform → the default voice.</p>
        <div class="row wrap"><input type="text" id="vName" placeholder="Name (as in chat)" style="max-width:240px" value="${esc(qs.get('user') || '')}"><select id="vPlat" style="max-width:160px"><option value="*">Any platform</option>${plats.map(p => `<option value="${p}" ${qs.get('p') === p ? 'selected' : ''}>${P[p].name}</option>`).join('')}</select><button class="primary" id="vAdd">Add person</button></div>
        <table class="t" style="margin-top:12px">${head}${Object.entries(users).map(([k, v]) => { const [p, n] = k.split(':'); return row('user', k, v, `<b>${esc(n)}</b> <span class="small muted">${p === '*' ? 'any platform' : esc(P[p] ? P[p].name : p)}</span>`); }).join('') || '<tr><td colspan="7" class="hint">Nobody yet - add a name above, or click a message in the chat panel.</td></tr>'}</table>`) +
        `<div style="height:14px"></div>` + card('👥 By role', `<table class="t">${head}${[['broadcaster', 'You'], ['moderator', 'Moderators'], ['vip', 'VIPs'], ['subscriber', 'Subscribers / members']].map(([k, t]) => row('role', k, roles[k] || {}, t)).join('')}</table>`) +
        `<div style="height:14px"></div>` + card('🌐 By platform', `<table class="t">${head}${plats.map(p => row('plat', p, pv[p] || {}, P[p].name)).join('')}</table>`);
      $('vAdd').onclick = async () => { const n = $('vName').value.trim(); if (!n) return toast('Type a name first', true); const r = await IXC.api('/api/tts/user', { name: n, platform: $('vPlat').value, voice: { enabled: true } }); if (!r.ok) return toast(r.error, true); await reload(); }; };
    const KEY = { user: 'tts.users', role: 'tts.roleVoices', plat: 'tts.platformVoices' };
    async function reload() { const d = await IXC.api('/api/settings'); if (d.values) S.values = d.values; paint(); }
    function read(tr) { const v = {}; tr.querySelectorAll('[data-f]').forEach(el => { const f = el.dataset.f; if (f === 'enabled') v.enabled = el.checked; else if (el.value !== '') v[f] = f === 'voice' ? el.value : +el.value; }); return v; }
    main.addEventListener('change', async (e) => { const tr = e.target.closest('tr[data-scope]'); if (!tr) return; const key = KEY[tr.dataset.scope]; const map = Object.assign({}, S.values[key] || {});
      const v = read(tr); if (tr.dataset.scope !== 'user' && !v.voice && v.speed == null && v.pitch == null && v.volume == null && v.enabled !== false) delete map[tr.dataset.key]; else map[tr.dataset.key] = v; await App.save({ [key]: map }); });
    main.addEventListener('click', async (e) => { const tr = e.target.closest('tr[data-scope]'); if (!tr) return;
      if (e.target.dataset.del) { const map = Object.assign({}, S.values['tts.users'] || {}); delete map[tr.dataset.key]; await App.save({ 'tts.users': map }); paint(); }
      if (e.target.dataset.test) { const v = read(tr); App.link.send({ type: 'tts.test', voice: v.voice || S.values['tts.voice'] }); } });
    paint();
  });

  // ======================= COMMANDS =======================
  page('obs', 'OBS Control', (main) => {
    // direct OBS connection (obs-websocket 5, built into OBS 28+): no Streamer.bot or plugin needed
    let scenes = null, sources = null, busy = false;
    const pill = (on, t) => `<span class="pill ${on ? 'st-connected' : ''}"><i></i>${t}</span>`;
    async function load() { const st = await IXC.api('/api/obs/state'); if (st.connected) { scenes = await IXC.api('/api/obs/scenes'); sources = scenes.ok ? await IXC.api('/api/obs/sources?scene=' + encodeURIComponent(scenes.current)) : null; } else { scenes = sources = null; } paint(st); }
    function renderCaps() { const el = $('oCaps'); if (!el) return; const c = Caps.data;
      if (!c) { el.innerHTML = card('🧾 What this OBS can do', '<p class="muted">Asking OBS…</p>'); return; }
      el.innerHTML = card('🧾 What this OBS can do <span class="small muted">(read-only report)</span>', !c.ok ? Caps.down(c, 'id="capsRefresh"') : `<div class="small">OBS ${esc(c.obs.obsVersion)} · obs-websocket ${esc(c.obs.obsWebSocketVersion)} · ${c.requestCount} requests · ${esc(c.obs.platformDescription || c.obs.platform || '')}</div>
        <div class="small" style="margin:6px 0">Main canvas ${c.videoSettings.baseWidth}×${c.videoSettings.baseHeight}, output ${c.videoSettings.outputWidth}×${c.videoSettings.outputHeight} at ${(c.videoSettings.fpsNumerator / c.videoSettings.fpsDenominator).toFixed(c.videoSettings.fpsDenominator === 1 ? 0 : 2)} fps · OBS CPU ${(+c.stats.cpuUsage || 0).toFixed(1)} % · ${Math.round(+c.stats.memoryUsage || 0)} MB · skipped by rendering ${c.stats.renderSkippedFrames || 0}, by the encoder ${c.stats.outputSkippedFrames || 0}</div>
        ${Caps.outputsTable(c)}
        <div class="small" style="margin-top:8px">OBS plugins that IXC can talk to: Aitum Multistream ${(Caps.vendor('aitum-multistream') || {}).registered ? 'detected' : 'not detected'} · Aitum Vertical Canvas ${(Caps.vendor('aitum-vertical-canvas') || {}).registered ? 'detected' : 'not detected'} · multiple canvases ${c.hasCanvasRequests ? 'known to OBS' : 'not reported'}</div>
        <div class="actions"><button class="small" id="capsRefresh">Refresh</button> <span class="small muted">${esc(c.generatedAt)} · IXC only asked OBS for status here; nothing was changed and no stream key or password was requested.</span></div>`); }
    function paint(st) { if (App.current !== 'obs') return;
      main.innerHTML = card('🎬 Connection', `<div class="row" style="gap:8px;flex-wrap:wrap">${UI.pill(st.connected ? 'connected' : st.state)} ${st.connected ? `<span class="small">OBS ${esc(st.version)} · WebSocket ${esc(st.websocketVersion)}</span>` : `<span class="small muted">${esc(st.detail || '')}</span>`}</div>
        ${st.connected ? `<div class="row" style="gap:8px;margin-top:8px">${pill(st.streaming, st.streaming ? 'Streaming' : 'Not streaming')} ${pill(st.recording, st.recording ? 'Recording' : 'Not recording')}</div>` : ''}
        <div class="field" style="margin-top:10px"><label class="lbl">Address</label><div class="val"><input type="text" id="oAddr" value="${esc(st.address === 'auto' ? '' : (st.address || '').replace(/^ws:\/\//, '').replace(/\/$/, ''))}" placeholder="automatic (from OBS's settings) - or 127.0.0.1:4455"></div></div>
        <div class="field"><label class="lbl">Password</label><div class="val"><input type="password" id="oPw" autocomplete="new-password" placeholder="${st.passwordSet ? '(saved - type a new one to replace it)' : 'automatic (from OBS\'s settings)'}"></div></div>
        <div class="actions"><button class="primary" id="oTest">${busy ? 'Testing…' : 'Save & test connection'}</button></div>
        <div class="hint">IXC finds OBS and its WebSocket password by itself. Only fill these in if OBS is on another PC or you changed the port: OBS > Tools > WebSocket Server Settings > Show Connect Info. OBS 28 or newer is needed.</div>`) +
        (scenes && scenes.ok ? `<div style="height:14px"></div>` + card('🎞️ Scenes', `<div class="row" style="gap:8px;flex-wrap:wrap">${scenes.scenes.map(n => `<button class="${n === scenes.current ? 'primary' : ''}" data-scene="${esc(n)}">${esc(n)}</button>`).join('')}</div>`) : '') +
        (sources && sources.ok ? `<div style="height:14px"></div>` + card('👁️ Sources in "' + esc(sources.scene) + '"', `<table class="t">${sources.sources.map(x => `<tr><td>${esc(x.name)}</td><td style="text-align:right"><button class="small ${x.visible ? '' : 'primary'}" data-src="${esc(x.name)}" data-vis="${x.visible ? 'hide' : 'show'}">${x.visible ? 'Hide' : 'Show'}</button></td></tr>`).join('')}</table>
          <div class="hint">Chat commands can do this too: Commands > edit a command > OBS action (e.g. !brb switches to your BRB scene).</div>`) : ''); main.insertAdjacentHTML('beforeend', '<div style="height:14px"></div><div id="oCaps"></div>'); renderCaps(); Caps.get().then(() => { if (App.current === 'obs') renderCaps(); }); }
    main.onclick = async (e) => { const b = e.target.closest('button'); if (!b) return; const d = b.dataset;
      if (b.id === 'capsRefresh') { b.textContent = 'Asking OBS…'; await Caps.get(true); renderCaps(); return; }
      if (b.id === 'oTest') { busy = true; b.textContent = 'Testing…'; const body = { address: $('oAddr').value.trim() || 'auto' }; if ($('oPw').value) body.password = $('oPw').value;
        const r = await IXC.api('/api/obs/connection', body); busy = false; toast(r.ok ? 'Connected to OBS ' + r.version : r.error, !r.ok); load(); }
      if (d.scene) { const r = await IXC.api('/api/obs/scene', { scene: d.scene }); if (!r.ok) toast(r.error, true); setTimeout(load, 300); }
      if (d.src) { const r = await IXC.api('/api/obs/source', { scene: sources.scene, source: d.src, visible: d.vis }); if (!r.ok) toast(r.error, true); load(); } };
    load(); on('status', () => { if (App.current === 'obs' && !busy) load(); });
  });
  page('audio', 'Audio routing', (main) => {
    // per-platform ON/OFF for music and the chat voice: switches OBS audio tracks (Routing.cs), read back from OBS
    const rows = [['music', '🎵 Music'], ['tts', '🔊 Chat voice (TTS)']]; const pl = ['twitch', 'kick', 'youtube']; const R = {};
    async function load() { for (const [s] of rows) R[s] = await IXC.api('/api/' + s + '/route'); paint(); }
    function paint() { if (App.current !== 'audio') return;
      main.innerHTML = card('🎚️ Who hears what on stream', `<table class="t" id="rtab"><tr><th></th>${pl.map(p => `<th><span class="tag ${p}">${P[p].tag}</span></th>`).join('')}<th></th></tr>` +
        rows.map(([s, n]) => { const r = R[s] || {}; const on = r.on || {}; return `<tr><td><b>${n}</b><div class="small ${r.ok ? 'muted' : 'warn'}">${esc(r.status || '')}</div></td>` +
          pl.map(p => `<td style="text-align:center"><label class="switch"><input type="checkbox" data-src="${s}" data-p="${p}" ${on[p] ? 'checked' : ''} aria-label="${esc(n + ' on ' + P[p].name)}"><i></i></label></td>`).join('') + '<td></td></tr>'; }).join('') + `</table>
        <div class="hint" style="margin-top:8px">A switch takes effect at once: IXC turns the OBS audio track of that platform on or off for the source and checks with OBS that it changed. Your microphone, game and other sources are never touched.</div>`) +
        `<div style="height:14px"></div>` + card('🧭 How this works (and its limits)', `<p class="small" style="margin-top:0">Windows can't send different sound to different websites. OBS can: each stream output records one <b>audio track</b>. This only works when each platform has its <b>own output</b> in OBS (a multistream plugin, or a second OBS output) set to its own track. With a single output that streams to one platform, use the switches of that platform only.</p>
          <p class="small">Tell IXC which track each platform's output uses:</p>` + pl.map(p => ctl.checks('music.routing.tracks.' + p, P[p].name + ' uses track', [1, 2, 3, 4, 5, 6].map(n => [String(n), String(n)]))).join('') +
          `<div class="hint">Tracks no platform uses (for example your recording track) are left as they are.</div>`);
      main.querySelectorAll('[data-src]').forEach(i => i.onchange = async () => { i.disabled = true; const r = await IXC.api('/api/' + i.dataset.src + '/route', { platform: i.dataset.p, on: i.checked }); R[i.dataset.src] = r; if (!r.ok) toast(r.error || r.status || 'OBS did not confirm the change', true); paint(); }); }
    load(); on('music', () => { if (App.current === 'audio' && S.route) { R.music = S.route; paint(); } });
  });
  page('timers', 'Timers', (main) => {
    // repeating chat messages, e.g. "Follow me everywhere" every 15 minutes (replaces Streamer.bot timers)
    let list = [], view = [];
    async function load() { const d = await IXC.api('/api/settings'); S.values = d.values || S.values; list = (S.values['timers.list'] || []).map(x => Object.assign({}, x)); await refresh(); }
    async function refresh() { const v = await IXC.api('/api/timers'); view = v.timers || []; paint(); }
    const st = (id) => (view.find(x => x.id === id) || {}).status || '';
    function paint() { if (App.current !== 'timers') return;
      main.innerHTML = card('⏱️ Timers', ctl.toggle('timers.enabled', 'Timers on') + ctl.range('timers.gapSec', 'At least this long between two timer messages', '', 's') +
        (list.length ? `<table class="t"><tr><th>On</th><th>Timer</th><th>Every</th><th>Needs</th><th>Status</th><th></th></tr>${list.map((t, i) => `<tr>
          <td><label class="switch"><input type="checkbox" data-en="${i}" ${t.enabled !== false ? 'checked' : ''} aria-label="On"><i></i></label></td>
          <td><b>${esc(t.name)}</b><div class="small muted">${esc(t.message)}</div><div class="tiny muted">${t.platforms && t.platforms.length ? t.platforms.map(p => P[p] ? P[p].name : p).join(', ') : 'All platforms'}${t.liveOnly ? ' · only while live' : ''}</div></td>
          <td class="small">${t.intervalMin} min</td><td class="small">${t.minLines ? t.minLines + ' chat msgs' : '-'}</td><td class="small">${esc(st(t.id))}</td>
          <td><button class="small" data-now="${i}">Send now</button> <button class="small" data-ed="${i}">Edit</button> <button class="small" data-rm="${i}">✕</button></td></tr>`).join('')}</table>` : '<p class="muted">No timers yet.</p>') +
        `<div class="actions"><button class="primary" id="tAdd">+ New timer</button></div>
        <div class="hint" style="margin-top:10px">A timer posts its message every few minutes, but only after enough people have chatted since its last message (so it never talks to an empty chat), and, with <b>only while live</b>, only on platforms where you are live. Messages can use {uptime} {viewers} {song}. IXC posts with your connected accounts (Platforms & accounts). A new timer waits one full interval before its first message.</div>`); }
    async function store() { const r = await IXC.api('/api/settings', { patch: { 'timers.list': list } }); if (!r.ok) { toast(r.error, true); await load(); return false; } S.values = r.values; list = r.values['timers.list'].map(x => Object.assign({}, x)); await refresh(); toast('Saved'); return true; }
    function edit(i) { const t = i < 0 ? { name: '', message: '', intervalMin: 15, minLines: 5, liveOnly: true, platforms: [], enabled: true } : Object.assign({}, list[i]);
      App.modal(`<h3>${i < 0 ? 'New timer' : 'Edit timer'}</h3>
        <div class="field"><label class="lbl">Name</label><div class="val"><input type="text" id="tN" value="${esc(t.name)}" placeholder="Socials"></div></div>
        <div class="field"><label class="lbl">Message</label><div class="val"><textarea id="tM" style="font-family:inherit" maxlength="450" placeholder="Follow me everywhere: kick.com/... | youtube.com/@...">${esc(t.message)}</textarea></div></div>
        <div class="field"><label class="lbl">Every (minutes)</label><div class="val"><input type="number" id="tI" min="1" max="240" value="${t.intervalMin}"></div></div>
        <div class="field"><label class="lbl">At least this many chat messages in between</label><div class="val"><input type="number" id="tL" min="0" max="500" value="${t.minLines}"></div></div>
        <div class="field"><label class="lbl">Only while live</label><div class="val"><label class="switch"><input type="checkbox" id="tLive" ${t.liveOnly ? 'checked' : ''} aria-label="Only while live"><i></i></label></div></div>
        <div class="field"><label class="lbl">Platforms</label><div class="val chipset" id="tPl">${App.PLAT_OPTS.filter(([v]) => v !== 'rumble').map(([v, n]) => `<label><input type="checkbox" value="${v}" ${!t.platforms.length || t.platforms.includes(v) ? 'checked' : ''}>${n}</label>`).join('')}</div></div>
        <div class="row" style="justify-content:flex-end;margin-top:14px"><button data-close>Cancel</button><button class="primary" id="tSave">Save</button></div>`);
      $('tSave').onclick = async () => { t.name = $('tN').value.trim(); t.message = $('tM').value.trim(); t.intervalMin = +$('tI').value; t.minLines = +$('tL').value; t.liveOnly = $('tLive').checked;
        const pl = [...document.querySelectorAll('#tPl input:checked')].map(x => x.value); t.platforms = pl.length === 3 ? [] : pl;
        if (i < 0) list.push(t); else list[i] = t; if (await store()) App.closeModal(); else if (i < 0) list.pop(); }; }
    main.onclick = async (e) => { const d = e.target.dataset; if (e.target.id === 'tAdd') edit(-1); if (d.ed) edit(+d.ed);
      if (d.now) { const r = await IXC.api('/api/timers/send', { id: list[+d.now].id }); toast(r.ok ? 'Sent to ' + r.sentTo.map(p => P[p].name).join(', ') : r.error, !r.ok); refresh(); }
      if (d.rm && await UI.confirmBox('Delete the timer "' + list[+d.rm].name + '"?', 'Delete')) { list.splice(+d.rm, 1); store(); } };
    main.onchange = (e) => { const i = e.target.dataset.en; if (i != null) { list[+i].enabled = e.target.checked; store(); } };
    load(); const iv = setInterval(() => { if (App.current !== 'timers') return clearInterval(iv); refresh(); }, 5000);
  });
  page('commands', 'Commands', (main) => {
    let list = [];
    async function load() { const d = await IXC.api('/api/settings'); S.values = d.values || S.values; list = (S.values['commands.list'] || []).map(x => Object.assign({}, x)); paint(); }
    const BUILT = { np: 'Says what\'s playing', queue: 'Says what\'s next', skip: 'Skips the song', uptime: 'How long you\'ve been live', wrongsong: 'Removes your last request', ttsskip: 'Skips the chat voice message', commands: 'Lists the commands', followage: 'How long the chatter has followed (Twitch)', clip: 'Makes a clip (Twitch) or saves the OBS replay buffer', ttson: 'Turns the chat voice on', ttsoff: 'Turns the chat voice off', viewers: 'Says the viewer count', pause: 'Pauses music', resume: 'Resumes music', songs: 'Custom text' };
    function paint() { main.innerHTML = card('⌨️ Chat commands', ctl.toggle('commands.enabled', 'Commands on') + `<table class="t"><tr><th>On</th><th>Command</th><th>Answer</th><th>Who</th><th>Cooldown</th><th></th></tr>${list.map((c, i) => `<tr><td><label class="switch"><input type="checkbox" data-en="${i}" ${c.enabled ? 'checked' : ''} aria-label="On"><i></i></label></td>
        <td><b>${esc(c.trigger)}</b>${c.aliases && c.aliases.length ? '<div class="tiny muted">' + esc(c.aliases.join(' ')) + '</div>' : ''}</td><td class="small">${c.builtin ? '<span class="muted">' + esc(BUILT[c.builtin] || '') + '</span>' + (c.response ? ' · ' : '') : ''}${esc(c.response)}</td>
        <td class="small">${esc(c.permission)}</td><td class="small">${c.cooldownSec ? c.cooldownSec + ' s' : '-'}</td><td><button class="small" data-ed="${i}">Edit</button> <button class="small" data-rm="${i}">✕</button></td></tr>`).join('')}</table>
        <div class="actions"><button class="primary" id="cAdd">+ New command</button><button id="cPresets">+ Ready-made commands</button><button id="cImport">Import from Streamer.bot</button><button id="cReset">Restore default commands</button></div>
        <div class="hint" style="margin-top:10px">In answers you can use {user} {name} {target} {args} {song} {title} {artist} {queue} {uptime} {viewers} {count} {platform}. Answers need IXC to be able to reply on that platform (Platforms & accounts).</div>`) +
        `<div style="height:14px"></div>` + card('🎉 Event messages', `<div class="hint" style="margin-bottom:8px">A chat message when something happens, posted on the platform where it happened. {user} is the person, {amount} the Super Chat amount.</div>` +
          EVENTS.map(([k, t]) => ctl.toggle('events.' + k + '.on', t) + ctl.text('events.' + k + '.message', 'Message')).join('') +
          ctl.range('events.cooldownSec', 'At most one message per event type and platform every', 'stops floods from follow bots and gift bombs', 's') +
          `<div class="hint">Follows: Twitch needs your connected Twitch account; Kick and Rumble work without. Subs, gifts and raids: Twitch and Kick. Super Chats and members: YouTube.</div>`); }
    const EVENTS = [['follow', 'New follower'], ['sub', 'New subscriber'], ['gift', 'Gifted subs'], ['raid', 'Raid / host'], ['superchat', 'Super Chat / Rant'], ['member', 'New YouTube member']];
    async function presets() { const r = await IXC.api('/api/commands/presets'); const todo = r.presets.filter(p => !p.present);
      if (!todo.length) { toast('You already have every ready-made command'); return; }
      App.modal(`<h3>Ready-made commands</h3><p class="small muted">Tick the ones you want. You can edit them afterwards.</p>
        ${todo.map(p => `<label class="row" style="gap:10px;align-items:flex-start;margin:8px 0"><input type="checkbox" data-pre="${esc(p.trigger)}" checked><span><b>${esc(p.trigger)}</b> <span class="small">${esc(p.response)}</span><div class="tiny muted">${esc(p.note)}</div></span></label>`).join('')}
        <div class="row" style="justify-content:flex-end;margin-top:14px"><button data-close>Cancel</button><button class="primary" id="preSave">Add</button></div>`);
      $('preSave').onclick = async () => { const t = [...document.querySelectorAll('[data-pre]:checked')].map(x => x.dataset.pre); const a = await IXC.api('/api/commands/presets', { triggers: t });
        if (!a.ok) { toast(a.error, true); return; } App.closeModal(); toast(a.added.length + ' command' + (a.added.length === 1 ? '' : 's') + ' added'); load(); }; }
    async function importSb() { const r = await IXC.api('/api/commands/streamerbot');
      if (!r.connected) { toast('Streamer.bot isn\'t connected - open it once (with its WebSocket server on) to import its commands', true); return; }
      if (!r.commands.length) { toast('Every Streamer.bot command is already in IXC'); return; }
      App.modal(`<h3>Import from Streamer.bot</h3><p class="small muted">Streamer.bot only shares the command names, not what its actions do - type each answer once. Empty ones are skipped.</p>
        ${r.commands.map((t, i) => `<div class="field"><label class="lbl"><b>${esc(t)}</b></label><div class="val"><input type="text" data-imp="${i}" placeholder="What IXC answers"></div></div>`).join('')}
        <div class="row" style="justify-content:flex-end;margin-top:14px"><button data-close>Cancel</button><button class="primary" id="impSave">Add commands</button></div>`);
      $('impSave').onclick = async () => { let n = 0; document.querySelectorAll('[data-imp]').forEach(el => { const v = el.value.trim(); if (v) { list.push({ trigger: r.commands[+el.dataset.imp], response: v, permission: 'everyone', cooldownSec: 10, userCooldownSec: 0, platforms: [], enabled: true, aliases: [] }); n++; } });
        if (!n) { App.closeModal(); return; } if (await store()) { App.closeModal(); toast(n + ' command' + (n === 1 ? '' : 's') + ' added'); } }; }
    async function store() { const r = await IXC.api('/api/settings', { patch: { 'commands.list': list } }); if (!r.ok) { toast(r.error, true); await load(); return false; } S.values = r.values; list = r.values['commands.list'].map(x => Object.assign({}, x)); paint(); toast('Saved'); return true; }
    function edit(i) { const c = i < 0 ? { trigger: '!', response: '', permission: 'everyone', cooldownSec: 10, userCooldownSec: 0, platforms: [], enabled: true, aliases: [] } : Object.assign({}, list[i]);
      App.modal(`<h3>${i < 0 ? 'New command' : 'Edit ' + esc(c.trigger)}</h3><div class="field"><label class="lbl">Command</label><div class="val"><input type="text" id="eT" value="${esc(c.trigger)}" ${c.builtin ? 'disabled' : ''}></div></div>
        <div class="field"><label class="lbl">Other names</label><div class="val"><input type="text" id="eA" value="${esc((c.aliases || []).join(' '))}" placeholder="!dc !disc"></div></div>
        <div class="field"><label class="lbl">Answer</label><div class="val"><textarea id="eR" style="font-family:inherit">${esc(c.response)}</textarea></div></div>
        <details ${Object.keys(c.responses || {}).length ? 'open' : ''}><summary class="small">Different answer per platform (optional)</summary>${App.PLAT_OPTS.map(([v, n]) => `<div class="field"><label class="lbl">${n}</label><div class="val"><input type="text" data-resp="${v}" value="${esc((c.responses || {})[v] || '')}" placeholder="(same as above)"></div></div>`).join('')}</details>
        <div class="field"><label class="lbl">OBS action</label><div class="val"><select id="eO">${[['', 'None'], ['scene', 'Switch to scene'], ['show', 'Show a source'], ['hide', 'Hide a source'], ['toggle', 'Show / hide a source']].map(([v, t]) => `<option value="${v}" ${((c.obs || {}).type || '') === v ? 'selected' : ''}>${t}</option>`).join('')}</select>
          <input type="text" id="eOS" placeholder="Scene" value="${esc((c.obs || {}).scene || '')}" style="margin-top:6px"><input type="text" id="eOX" placeholder="Source (for show / hide)" value="${esc((c.obs || {}).source || '')}" style="margin-top:6px"></div></div>
        <div class="field"><label class="lbl">Who can use it</label><div class="val"><select id="eP">${App.ROLE_OPTS.map(([v, t]) => `<option value="${v}" ${c.permission === v ? 'selected' : ''}>${esc(t)}</option>`).join('')}</select></div></div>
        <div class="field"><label class="lbl">Cooldown (s)</label><div class="val"><input type="number" id="eC" min="0" max="3600" value="${c.cooldownSec}"></div></div>
        <div class="field"><label class="lbl">Per person cooldown (s)</label><div class="val"><input type="number" id="eU" min="0" max="3600" value="${c.userCooldownSec}"></div></div>
        <div class="field"><label class="lbl">Platforms</label><div class="val chipset" id="ePl">${App.PLAT_OPTS.map(([v, t]) => `<label><input type="checkbox" value="${v}" ${!c.platforms.length || c.platforms.includes(v) ? 'checked' : ''}>${t}</label>`).join('')}</div></div>
        <div class="row" style="justify-content:flex-end;margin-top:14px"><button data-close>Cancel</button><button class="primary" id="eSave">Save</button></div>`);
      $('eSave').onclick = async () => { c.trigger = $('eT').value.trim(); c.aliases = $('eA').value.split(/[\s,]+/).filter(Boolean); c.response = $('eR').value.trim(); c.responses = {}; document.querySelectorAll('[data-resp]').forEach(el => { if (el.value.trim()) c.responses[el.dataset.resp] = el.value.trim(); }); c.permission = $('eP').value; c.obs = $('eO').value ? { type: $('eO').value, scene: $('eOS').value.trim(), source: $('eOX').value.trim() } : null; c.cooldownSec = +$('eC').value; c.userCooldownSec = +$('eU').value;
        const pl = [...document.querySelectorAll('#ePl input:checked')].map(x => x.value); c.platforms = pl.length === 4 ? [] : pl;
        if (i < 0) list.push(c); else list[i] = c; if (await store()) App.closeModal(); else if (i < 0) list.pop(); }; }
    main.onclick = async (e) => { const d = e.target.dataset; if (e.target.id === 'cAdd') edit(-1); if (e.target.id === 'cImport') importSb(); if (e.target.id === 'cPresets') presets(); if (d.ed) edit(+d.ed);
      if (d.rm && await UI.confirmBox('Delete ' + list[+d.rm].trigger + '?', 'Delete')) { list.splice(+d.rm, 1); store(); }
      if (e.target.id === 'cReset' && await UI.confirmBox('Put back the default commands? Your own commands stay.', 'Restore')) { const own = list.filter(c => !c.builtin); const r = await IXC.api('/api/settings', { patch: { 'commands.list': null } }); list = (r.values ? r.values['commands.list'] : []).concat(own); store(); } };
    main.onchange = (e) => { const i = e.target.dataset.en; if (i != null) { list[+i].enabled = e.target.checked; store(); } };
    load();
  });

  // ======================= FILTERS =======================
  page('filters', 'Chat filters', (main) => {
    const A = [['off', 'Off'], ['tts', "Don't read it out"], ['hide', 'Hide on stream (and don\'t read)']];
    main.innerHTML = `<p class="muted" style="margin-top:0">Filtered messages still appear (dimmed, with the reason) in your chat panel, so you never miss anything. Moderators and you are never filtered${''}.</p><div class="grid2"><div>` +
      card('🛡 Filters', ctl.toggle('chat.filters.enabled', 'Filters on') + ctl.select('chat.filters.minRole', 'Show on stream', App.ROLE_OPTS, 'e.g. subscribers-only overlay') +
        ctl.select('chat.filters.links', 'Links', A) + ctl.select('chat.filters.caps', 'Lots of CAPITALS', A) + ctl.range('chat.filters.capsPercent', 'Counts as caps from', '', '%') +
        ctl.select('chat.filters.repeatChars', 'Repeated characters (aaaaaaaa)', A) + ctl.range('chat.filters.maxRepeat', 'Allowed repeats') + ctl.select('chat.filters.longMessage', 'Very long messages', A) + ctl.range('chat.filters.maxLength', 'Longer than', 'characters') +
        ctl.select('chat.filters.duplicate', 'Same person repeating', A) + ctl.range('chat.filters.duplicateSec', 'Within', '', 's') + ctl.select('chat.filters.copyPaste', 'Copy-paste spam (many people, same text)', A) + ctl.select('chat.filters.suspicious', 'Weird characters / symbol walls', A) +
        ctl.toggle('chat.filters.modsBypass', 'Moderators skip filters') + ctl.toggle('chat.filters.subsBypass', 'Subscribers skip filters')) +
      `</div><div>` + card('🚫 Words and people', ctl.select('chat.filters.bannedWords', 'Banned words', A) + ctl.list('chat.filters.bannedWordList', 'Banned words', 'one per line, * = anything (e.g. scam*)') + ctl.list('chat.filters.blacklist', 'Always hide these people') + ctl.list('chat.filters.whitelist', 'Never filter these people')) +
      `<div style="height:14px"></div>` + card('📊 Filtered so far', `<div id="fStats" class="kv"></div>`) + `</div></div>`;
    IXC.api('/api/diag').then(d => { const f = d.filters || {}; $('fStats').innerHTML = Object.keys(f).length ? Object.entries(f).map(([k, v]) => `<span>${esc(k)}</span><span>${v}</span>`).join('') : '<span class="muted">nothing yet</span>'; });
  });

  // ======================= SYSTEM CHECK =======================
  page('check', 'System check', (main) => {
    main.innerHTML = `<div class="grid2"><div><div class="card"><h3>🩺 IXC system check <span class="grow"></span><button class="primary" id="run">Run check</button></h3><div id="res"><div class="hint">Checks OBS, platforms, voice, music, phone, internet and settings.</div></div></div>
      <div style="height:14px"></div><div class="card"><h3>🔧 Repair</h3><div class="list" id="tools"></div></div></div>
      <div><div class="card"><h3>⬆️ Updates</h3><div id="upd"></div></div><div style="height:14px"></div>
      <div class="card"><h3>💾 Backups</h3><div class="hint">IXC backs up your settings, sign-ins, phones, commands, voices and queue before every update. Restoring restarts IXC.</div><div class="list" id="bk" style="margin-top:8px"></div><div class="actions"><button id="bkNew">Back up now</button></div></div>
      <div style="height:14px"></div><div class="card"><h3>📦 Diagnostics</h3><div class="hint">Makes a ZIP with logs and system details for support. Passwords, keys and sign-in tokens are never included.</div><div class="actions"><button id="diag">Export diagnostics</button><button data-open="logs">Open logs folder</button></div><div id="diagRes"></div></div>
      <div style="height:14px"></div><div class="card"><h3>📜 Recent events <span class="grow"></span><select id="logArea" style="width:auto"><option value="">All</option>${['app', 'obs', 'chat', 'tts', 'music', 'phone', 'auth', 'net', 'streamerbot', 'overlay', 'update'].map(a => `<option>${a}</option>`).join('')}</select></h3><div class="logs" id="logs"></div></div></div></div>`;
    const TOOLS = [['connections.all', 'Reconnect everything', 'OBS, chat platforms, phone'], ['obs.setup', 'Repair OBS sources', 'adds the music player and chat voice to OBS again'], ['obs.docks', 'Add IXC panels to OBS', 'needs OBS closed'], ['obs.websocket', 'Repair OBS connection', 'turns on OBS\'s WebSocket server (OBS closed)'],
      ['audio.repair', 'Repair music audio', 'restores the music volume and the platform switch'], ['tts.repair', 'Repair chat voice', 'clears the queue, restores music volume'], ['music.reload', 'Reload the current song', ''], ['overlay.reload', 'Reload all overlays and panels', ''],
      ['phone.reconnect', 'Reconnect the phone relay', ''], ['streamerbot.reconnect', 'Reconnect Streamer.bot', 'optional'], ['cache.clear', 'Clear caches', ''], ['reinstall', 'Reinstall IXC (keeps your settings)', 'downloads this version again'], ['config.reset', 'Reset all settings', 'a backup is made first']];
    $('tools').innerHTML = TOOLS.map(([a, t, h]) => `<div class="item"><div class="t"><b>${esc(t)}</b><small>${esc(h)}</small></div><button class="small" data-fix="${a}">Run</button></div>`).join('');
    async function run() { const b = $('run'); b.disabled = true; b.innerHTML = '<span class="spin"></span> Checking…'; const r = await IXC.api('/api/health'); b.disabled = false; b.textContent = 'Run check again';
      const ic = { ok: '✓', warn: '!', error: '✕', off: '–' };
      $('res').innerHTML = `<div class="banner ${r.overall === 'healthy' ? 'ok' : r.overall === 'error' ? 'err' : ''} section">${r.overall === 'healthy' ? '✓ Everything looks good' : r.overall === 'error' ? 'Some things need fixing' : 'Mostly fine - a few things to check'}</div>` +
        (r.items || []).map(i => `<div class="check ${i.level}"><span class="ico">${ic[i.level] || '?'}</span><div class="grow"><b>${esc(i.name)}</b><div class="m">${esc(i.message)}</div></div>${i.fix ? `<button class="small ${i.level === 'error' ? 'primary' : ''}" data-fix="${esc(i.fix)}">${esc(i.fixLabel || 'Fix automatically')}</button>` : ''}</div>`).join(''); }
    $('run').onclick = run;
    main.addEventListener('click', async (e) => { const b = e.target.closest('button'); if (!b) return;
      if (b.dataset.fix) { if (b.dataset.fix === 'config.reset' && !await UI.confirmBox('Reset all IXC settings to defaults? A backup is made first.', 'Reset')) return; const r = await repair(b.dataset.fix, b); if (r && r.ok && b.closest('#res')) setTimeout(run, 1500); }
      if (b.dataset.restore && await UI.confirmBox('Restore this backup? IXC restarts.', 'Restore')) { const r = await IXC.api('/api/backups', { restore: b.dataset.restore }); toast(r.ok ? r.message : r.error, !r.ok); }
      if (b.dataset.open) IXC.api('/api/open', { what: b.dataset.open }); });
    $('bkNew').onclick = async () => { const r = await IXC.api('/api/backups', {}); toast(r.ok ? 'Backup made' : r.error, !r.ok); backups(); };
    $('diag').onclick = async (e) => { e.target.disabled = true; e.target.innerHTML = '<span class="spin"></span> Collecting…'; const r = await IXC.api('/api/diagnostics/export', {}); e.target.disabled = false; e.target.textContent = 'Export diagnostics';
      $('diagRes').innerHTML = r.ok ? `<div class="banner ok" style="margin-top:8px">Saved: ${esc(r.name)} <span class="grow"></span><button data-open="exports">Show file</button></div>` : `<div class="banner err">${esc(r.error)}</div>`; };
    async function backups() { const d = await IXC.api('/api/backups'); $('bk').innerHTML = (d.backups || []).slice(0, 8).map(b => `<div class="item"><div class="t"><b>${esc(b.name.replace(/\.zip$/, '').replace(/_/g, ' '))}</b><small>${esc(b.at)}</small></div><button class="small" data-restore="${esc(b.name)}">Restore</button></div>`).join('') || '<div class="hint">No backups yet.</div>'; }
    async function logs() { const a = $('logArea'); if (!a) return; const d = await IXC.api('/api/logs?n=150' + (a.value ? '&area=' + a.value : '')); const el = $('logs'); const bottom = el.scrollHeight - el.scrollTop - el.clientHeight < 30;
      el.innerHTML = (d.entries || []).map(x => `<div class="${x.level}">${esc(x.at)} [${esc(x.area)}] ${esc(x.msg)}</div>`).join(''); if (bottom) el.scrollTop = el.scrollHeight; }
    function upd() { const u = (S.status || {}).update || {}; $('upd').innerHTML = `<div class="kv"><span>Installed</span><span>${esc(u.current || '')}</span><span>Latest</span><span>${esc(u.latest || 'not checked yet')}</span></div>${u.error ? `<div class="warn small" style="margin-top:6px">${esc(u.error)}</div>` : ''}${u.progress ? `<div class="small" style="margin-top:6px">${esc(u.progress)}</div>` : ''}${u.available && !u.busy && u.auto && u.autoNote ? `<div class="small muted" style="margin-top:6px">${esc(/^after/.test(u.autoNote) ? 'IXC installs it by itself ' + u.autoNote + '.' : u.autoNote)}</div>` : ''}
      <div class="actions"><button id="uChk">Check now</button>${u.available ? `<button class="primary" data-fix="update.install">Update to ${esc(u.latest)}</button>` : ''}</div>${u.available && u.notes ? `<details style="margin-top:8px"><summary>What's new</summary><div class="small" style="white-space:pre-wrap">${esc(u.notes)}</div></details>` : ''}`;
      $('uChk').onclick = async () => { const r = await IXC.api('/api/update/check'); S.status.update = r; upd(); toast(r.available ? 'An update is available' : r.error || 'IXC is up to date', !!r.error); }; }
    $('logArea').onchange = logs; backups(); logs(); upd(); on('status', upd);
    // not checked yet (IXC just started): check now instead of showing "not checked yet"
    if (!((S.status || {}).update || {}).checked) IXC.api('/api/update/check').then(r => { if (r && S.status) { S.status.update = r; upd(); } }).catch(() => {}); const lt = setInterval(() => { if (App.current !== 'check') return clearInterval(lt); logs(); }, 3000);
    if (location.hash.includes('run')) run();
  });

  // ======================= SETTINGS =======================
  page('settings', 'Settings', (main) => {
    const paint = () => { const adv = S.values['general.advanced'];
      main.innerHTML = `<div class="grid2"><div>` + card('⚙️ General', ctl.toggle('general.followObs', 'Start and stop with OBS', 'at sign-in only the small tray icon starts; IXC opens when OBS opens and closes after OBS closes. Off = IXC runs all the time') + ctl.range('general.obsGraceSec', 'Wait after OBS closes', 'so a quick OBS restart doesn\'t stop IXC', 's') + ctl.toggle('general.autoRestart', 'Restart IXC by itself if it stops unexpectedly') + ctl.toggle('general.checkUpdates', 'Check for updates') + ctl.toggle('general.autoUpdate', 'Install updates by itself (never while you stream or record)') + ctl.toggle('general.openDashboardOnStart', 'Open this window when Windows starts') +
        ctl.range('viewers.refreshSec', 'Refresh viewer counts every', '', 's') + ctl.toggle('remote.enabled', 'Phone remote', 'lets paired phones control IXC') + ctl.range('remote.deviceDays', 'Forget phones not used for', '', 'd') + ctl.toggle('general.advanced', 'Advanced mode', 'shows technical options')) +
        `<div style="height:14px"></div>` + card('🔁 IXC', `<div class="actions"><button id="sRestart">Restart IXC</button><button id="sQuit">Quit IXC</button><button data-open="data">Open IXC's data folder</button><button id="sWiz">Run setup again</button></div>`) + `</div><div>` +
        (adv ? card('🔧 Advanced', ctl.toggle('diagnostics.verboseLog', 'Detailed logs') + ctl.range('helper.port', 'IXC port', 'restart IXC after changing; IXC updates its OBS sources by itself') + ctl.text('obs.websocketUrl', 'OBS WebSocket address', 'auto', '"auto" reads it from OBS') +
          ctl.text('remote.relayUrl', 'Phone relay address', 'built in', 'leave empty for the built-in one') + ctl.text('platforms.streamerbot.websocketUrl', 'Streamer.bot address') + ctl.text('platforms.streamerbot.settingsPath', 'Streamer.bot settings file', 'auto') +
          ctl.text('chat.extraCommandsFile', 'Extra commands file', '', 'a text/code file to read ! commands from (for suggestions)') + ctl.list('chat.hiddenCommands', 'Hide these commands from suggestions') +
          `<div class="actions"><button data-fix="phone.reset">New phone address (unpairs all phones)</button></div>`) : card('🔧 Advanced', '<div class="hint">Turn on Advanced mode to see technical options. You normally never need them.</div>')) + `</div></div>`;
      $('sRestart').onclick = async () => { await IXC.api('/api/system/restart', {}); toast('Restarting…'); };
      $('sQuit').onclick = async () => { if (await UI.confirmBox('Quit IXC? Music, chat voice and the phone remote stop until IXC starts again (Start menu > IXC).', 'Quit')) { await IXC.api('/api/system/quit', {}); toast('IXC stopped'); } };
      $('sWiz').onclick = () => Wizard.open(true);
      main.querySelectorAll('[data-open]').forEach(b => b.onclick = () => IXC.api('/api/open', { what: b.dataset.open }));
      main.querySelectorAll('[data-fix]').forEach(b => b.onclick = async () => { if (await UI.confirmBox('All phones will need a new QR code. Continue?', 'Continue')) repair(b.dataset.fix, b); }); };
    paint(); on('settings', () => { if (document.activeElement && document.activeElement.closest('#main')) return; paint(); });
  });
  // ======================= MULTISTREAM =======================
  // Multistreaming and the vertical canvas are done in OBS by the Aitum plugins; IXC's own studio pages were not working, so they are off the menu.
  // Old links land on this note; the old pages stay under #multistream-legacy / #vertical-legacy (backend unchanged).
  // Streams: Twitch / YouTube / Kick as OBS and its plugins have them set up (read-only; /api/outputs never returns a stream key).
  // "STREAMING" means OBS reports the output active (local), not that the platform confirmed it receives the stream.
  page('multistream', 'Streams', (main) => {
    const ST = { 'STREAMING': 'connected', 'CONNECTING': 'connecting', 'READY': 'idle', 'ERROR': 'error', 'NOT CONFIGURED': 'off', 'DISCONNECTED': 'disconnected' };
    const NAME = { twitch: 'Twitch', youtube: 'YouTube', kick: 'Kick' };
    async function load() { const r = await IXC.api('/api/outputs'); if (App.current !== 'multistream') return; paint(r); }
    function paint(r) {
      if (!r || !r.ok) { main.innerHTML = card('📡 Streams', `<div class="err">${esc((r && r.error) || 'IXC could not read the stream settings')}</div>`); return; }
      main.innerHTML = `<div class="actions" style="margin:0 0 10px"><button class="primary" id="outCheck">Check all</button><span class="small muted" style="align-self:center">OBS: ${esc(r.obs)} · start and stop each platform in OBS's Aitum Multistream dock</span></div>`
        + (r.mainIssue ? `<div class="banner warn" style="margin-bottom:10px">${esc(r.mainIssue)}</div>` : '')
        + `<div class="grid">` + ['twitch', 'youtube', 'kick'].map(p => { const x = r.platforms[p];
          return `<div class="card"><div class="plat"><span class="logo2 ${p}">${NAME[p].slice(0, 2)}</span><div class="grow"><h3 style="margin:0">${NAME[p]}</h3></div>${UI.pill(ST[x.state] || 'off', x.state)}</div>`
            + (x.liveNote ? `<div class="small" style="margin-top:6px">${esc(x.liveNote)}</div>` : '')
            + (x.outputs.length ? `<ul class="small" style="margin:6px 0 0 16px;padding:0">${x.outputs.map(o => `<li>${esc(o.via)}: <b>${esc(o.name)}</b> · ${o.serverSet ? esc(o.server) : '<span class="err">no server</span>'} · key ${o.keySet ? 'set' : '<span class="err">not set</span>'} · audio track ${o.audioTrack}</li>`).join('')}</ul>` : `<div class="small muted" style="margin-top:6px">No output for ${NAME[p]} in OBS. Add one in the Aitum Multistream dock (server address + stream key from ${NAME[p]}'s dashboard).</div>`)
            + (x.issues.length ? `<ul class="small err" style="margin:6px 0 0 16px;padding:0">${x.issues.map(i => `<li>${esc(i)}</li>`).join('')}</ul>` : '') + `</div>`; }).join('') + `</div>`
        + `<p class="tiny muted">${esc(r.note)}${r.otherActiveOutputs && r.otherActiveOutputs.length ? ' Other active OBS outputs: ' + esc(r.otherActiveOutputs.join(', ')) : ''}</p>`;
      $('outCheck').onclick = load; }
    main.innerHTML = '<p class="muted">Reading OBS…</p>'; load();
    const iv = setInterval(() => { if (App.current !== 'multistream') return clearInterval(iv); load(); }, 5000); });
  for (const [id, title] of [['vertical', 'Vertical stream']]) page(id, title, (main) => {
    main.innerHTML = card('📡 ' + title + ' is done in OBS', `<p class="small" style="margin-top:0">Use the <b>Aitum Multistream</b> dock in OBS to start and stop each platform, and its settings for bitrate and encoder. IXC shows OBS's real stream and output status under <a href="#obs">OBS Control</a>, and sends music and chat voice to each platform under <a href="#audio">Audio routing</a>.</p><p class="small">If OBS &gt; Settings &gt; Stream points at <code>rtmp://127.0.0.1:19350/live</code>, that was IXC's old relay: set it to your main platform instead.</p>`); });
  // One OBS stream, forwarded by IXC to up to 8 destinations. Stream keys are write-only: typed here, stored encrypted, never shown again.
  page('multistream-legacy', 'Multistream Studio (old)', (main) => {
    let d = null, showIngest = false;
    const MAX = 8;
    async function load() { const r = await IXC.api('/api/multistream'); if (r && r.destinations) { d = r; paint(); } Caps.get().then(() => { if (App.current === 'multistream-legacy') paint(); }); }
    const fmtMB = (n) => (n || 0).toFixed(n >= 10 ? 0 : 1);
    function publishing(x) {   // is the platform actually receiving the stream? (not the same as "the server answers")
      const r = x.relay || {};
      if (r.state === 'live') return ['live', 'Publishing - the server accepted the stream (' + fmtMB(r.sentMB) + ' MB sent' + (r.droppedFrames ? ', ' + r.droppedFrames + ' video frames skipped' : '') + ')'];
      if (r.state === 'connecting') return ['connecting', 'Connecting to the server…'];
      if (r.state === 'reconnecting') return ['reconnecting', 'Not publishing: ' + (r.detail || 'connection lost')];
      if (r.state === 'skipped') return ['off', 'Not used for this stream: ' + (r.detail || '')];
      return ['off', d.relay.running ? (d.relay.publishing ? 'Not publishing' : 'Waiting for OBS to start streaming') : 'Not publishing (the relay is off)']; }
    function reach(x) {   // "server reachable" only means TCP/TLS and the RTMP handshake worked - not that the key is right
      if (x.state === 'checking') return ['connecting', 'Checking the server…'];
      if (x.state === 'ok') return ['connected', 'Server reachable (handshake only - this does not prove the key is accepted)'];
      if (x.state === 'failed') return ['error', 'Server not reachable: ' + x.detail];
      return ['off', 'Server not checked']; }
    function studioStatus() { const c = Caps.data; const head = '<h3 style="margin:0 0 8px">Stream status <span class="small muted">· from OBS</span></h3>';
      if (!c) return card('📡 OBS', head + '<p class="muted">Asking OBS…</p>');
      if (!c.ok) return card('📡 OBS', head + Caps.down(c, 'data-act="capsRefresh"'));
      const ss = c.streamStatus || {}; const live = c.outputs.filter(o => o.active).length;
      return card('📡 OBS', head + '<div class="row" style="gap:10px;flex-wrap:wrap">' + UI.pill(ss.outputActive ? 'live' : 'off', ss.outputActive ? 'OBS is streaming (' + esc(ss.outputTimecode || '') + ')' : 'OBS is not streaming') + '<span class="small muted">' + live + ' of ' + c.outputCount + ' OBS outputs active · main canvas ' + c.videoSettings.outputWidth + '×' + c.videoSettings.outputHeight + ' @ ' + (c.videoSettings.fpsNumerator / c.videoSettings.fpsDenominator) + ' fps (set in OBS: Settings › Video / Output)</span></div>' + Caps.outputsTable(c) +
        '<div class="actions"><button class="small" data-act="capsRefresh">Refresh</button></div>'); }
    function independentCard() { const c = Caps.data, v = c && c.ok ? Caps.vendor('aitum-multistream') : null;
      return '<div style="height:18px"></div>' + card('🎛️ Independent outputs <span class="small muted">· own encoder, bitrate or size per destination</span>', '<div style="margin-bottom:8px">' + NOTIMPL('IXC cannot create or configure these yet.') + '</div>' +
        '<div class="small">Giving a destination its own encoder, bitrate or resolution needs an OBS-side output (an OBS plugin, with its own encoder in OBS). ' + (v && v.registered ? 'Aitum Multistream is detected in OBS' + (v.answer && v.answer.version ? ' (version ' + esc(v.answer.version) + ')' : '') + ': set its destinations up inside OBS. IXC does not edit them yet; start/stop control from IXC has not been built or tested.' : 'No such plugin is detected in OBS' + (c && c.ok ? '' : ' (OBS is not connected)') + '.') + '</div>' +
        '<div class="hint" style="margin-top:8px">Today: the shared-encode relay above, and OBS\'s own single stream. Nothing on this page claims per-destination bitrate or encoder control.</div>'); }
    function relayCard() {
      const r = d.relay, st = !r.running ? ['off', 'Off'] : r.publishing ? ['live', 'OBS is sending - forwarding to your destinations'] : ['waiting', 'On - waiting for OBS to start streaming'];
      const obs = r.running ? `<div class="hint" style="margin-top:10px"><b>Put this in OBS</b> (Settings › Stream › Service: <b>Custom…</b>) - IXC does not change OBS for you:
        <div class="field"><label class="lbl">Server</label><div class="val"><input type="text" id="msSrv" readonly value="${esc(r.serverUrl)}"> <button class="small" data-act="copy" data-v="${esc(r.serverUrl)}">Copy</button></div></div>
        <div class="field"><label class="lbl">Stream key</label><div class="val"><input type="${showIngest ? 'text' : 'password'}" id="msKey" readonly value="${esc(r.streamKey)}" autocomplete="off"> <button class="small" data-act="showkey">${showIngest ? 'Hide' : 'Show'}</button> <button class="small" data-act="copy" data-v="${esc(r.streamKey)}">Copy</button></div></div>
        <div class="small muted">This key only lets OBS on this PC talk to IXC. It is not a platform key. Keep OBS's own encoder settings (bitrate, keyframe interval 2 s) as the platforms require.</div></div>` : '';
      return card('📡 Relay', `<div class="row" style="gap:12px;align-items:center">${UI.pill(st[0], st[1])}<span class="grow"></span>
        ${r.running ? '<button data-act="relayStop" id="msStop">Stop relay</button>' : '<button class="primary" data-act="relayStart" id="msStart">Start relay</button>'}</div>${obs}
        <div class="hint" style="margin-top:10px">The relay is off until you start it, and it does not start by itself. OBS encodes once; IXC copies that same stream to every destination below - nothing is re-encoded, so there is almost no extra load. Stopping OBS's stream ends all destinations.</div>`); }
    function destCard(x) {
      const p = publishing(x), c = reach(x), full = x.keySet ? '<span class="small">🔒 Key saved - hidden</span>' : '<span class="small muted">No stream key yet</span>';
      return `<div class="card" data-id="${esc(x.id)}"><div class="row" style="align-items:center;gap:10px"><h3 style="margin:0" class="grow">${esc(x.name)} <span class="small muted">· ${esc(x.platform)}</span></h3>
        <label class="switch"><input type="checkbox" data-en="${esc(x.id)}" ${x.enabled ? 'checked' : ''} aria-label="Use ${esc(x.name)}"><i></i></label></div>
        <div class="small muted" style="margin:6px 0">${esc(x.url)} · ${full}</div>
        <div class="row" style="gap:8px;flex-wrap:wrap">${UI.pill(p[0], p[1])}</div>
        <div class="row" style="gap:8px;flex-wrap:wrap;margin-top:6px">${UI.pill(c[0], c[1])}</div>
        <div class="actions" style="margin-top:10px"><button class="small" data-act="check" data-id="${esc(x.id)}">Check server</button> <button class="small" data-act="edit" data-id="${esc(x.id)}">Edit</button> <button class="small" data-act="del" data-id="${esc(x.id)}">Delete</button></div></div>`; }
    function paint() { if (App.current !== 'multistream-legacy' || !d) return;
      if (document.getElementById('msModal')) return;   // never redraw under an open editor
      const n = d.destinations.length;
      main.innerHTML = studioStatus() + `<h3 style="margin:18px 0 4px">Shared-encode destinations <span class="small muted">· IXC relay</span></h3><div class="small muted" style="margin-bottom:8px">Every destination here receives exactly the stream OBS encodes: one encoder, no extra encoding load, and the same bitrate, resolution and codecs for all. IXC cannot give one of them its own bitrate or encoder.</div>` + relayCard() + `<h3 style="margin:18px 0 8px">Destinations <span class="small muted">(${n} of ${MAX})</span></h3>` +
        (n ? d.destinations.map(destCard).join('') : '<p class="muted">No destinations yet. Add the server address and stream key each platform shows in its own dashboard.</p>') +
        `<div class="actions"><button class="primary" id="msAdd" ${n >= MAX ? 'disabled title="IXC manages up to ' + MAX + ' destinations"' : ''}>+ Add destination</button>${n >= MAX ? ' <span class="small muted">The limit is ' + MAX + ' destinations.</span>' : ''}</div>
        <div class="hint" style="margin-top:12px"><b>Server reachable</b> only proves IXC can open a connection and speak RTMP with that address - it does not send your key. <b>Publishing</b> means the platform accepted your stream key and is receiving video. Use a throw-away or private stream first. Each platform has its own rules (accounts, minimum followers, keys that expire); copy the address and key from its dashboard.</div>` + independentCard(); }
    function edit(id) {
      const x = id ? d.destinations.find(z => z.id === id) : { platform: 'custom', name: '', url: '', enabled: true, keySet: false };
      const pre = (pl) => d.presets.find(z => z.platform === pl) || d.presets[d.presets.length - 1];
      App.modal(`<div id="msModal"><h3>${id ? 'Edit destination' : 'Add destination'}</h3>
        <div class="field"><label class="lbl">Platform</label><div class="val"><select id="mP">${d.presets.map(z => `<option value="${esc(z.platform)}" ${z.platform === x.platform ? 'selected' : ''}>${esc(z.label)}</option>`).join('')}</select></div></div>
        <div class="hint small" id="mNote">${esc(pre(x.platform).note)}</div>
        <div class="field"><label class="lbl">Name</label><div class="val"><input type="text" id="mN" maxlength="40" value="${esc(x.name)}" placeholder="My Twitch"></div></div>
        <div class="field"><label class="lbl">Server URL</label><div class="val"><input type="text" id="mU" value="${esc(x.url)}" placeholder="rtmp://… or rtmps://…" spellcheck="false" autocomplete="off"><div class="tiny muted" id="mS"></div></div></div>
        <div class="field"><label class="lbl">Stream key</label><div class="val"><input type="password" id="mK" autocomplete="new-password" spellcheck="false" placeholder="${x.keySet ? '•••••••• saved - leave empty to keep it' : 'paste the key from the platform'}">
          ${x.keySet ? '<label class="small"><input type="checkbox" id="mC"> Remove the saved key</label>' : ''}<div class="tiny muted">Stored encrypted on this PC. It is never shown again.</div></div></div>
        <div class="field"><label class="lbl">Use this destination</label><div class="val"><label class="switch"><input type="checkbox" id="mE" ${x.enabled ? 'checked' : ''} aria-label="Enabled"><i></i></label></div></div>
        <div class="row" style="justify-content:flex-end;margin-top:14px"><button data-close>Cancel</button><button class="primary" id="mSave">Save</button></div></div>`);
      let lastSug = '';
      const sug = () => { const z = pre($('mP').value); $('mNote').textContent = z.note; $('mS').textContent = z.url && z.suggested ? 'Suggested address - confirm it in the platform\'s dashboard.' : ''; };
      $('mP').onchange = () => { const z = pre($('mP').value); const u = $('mU').value.trim(); if ((u === '' || u === lastSug) && z.url) { $('mU').value = z.url; lastSug = z.url; } if (!$('mN').value.trim() || d.presets.some(q => q.label === $('mN').value.trim())) $('mN').value = z.label === 'Custom' ? '' : z.label; sug(); }; sug();
      $('mSave').onclick = async () => {
        const body = { platform: $('mP').value, name: $('mN').value.trim(), url: $('mU').value.trim(), enabled: $('mE').checked }; if (id) body.id = id;
        const k = $('mK').value.trim(); if (k) body.key = k; if ($('mC') && $('mC').checked) body.clearKey = true;
        const r = await IXC.api('/api/multistream/save', body); $('mK').value = ''; if (!r.ok) { toast(r.error || 'Could not save', true); return; } App.closeModal(); toast('Saved'); load(); }; }
    main.onclick = async (e) => { const t = e.target.closest('[data-act],#msAdd'); if (!t) return; const a = t.dataset.act, id = t.dataset.id;
      if (t.id === 'msAdd') edit(null);
      else if (a === 'edit') edit(id);
      else if (a === 'del') { const x = d.destinations.find(z => z.id === id); if (await UI.confirmBox('Delete "' + x.name + '" and its saved stream key?', 'Delete')) { const r = await IXC.api('/api/multistream/delete', { id }); toast(r.ok ? 'Deleted' : r.error, !r.ok); load(); } }
      else if (a === 'check') { const r = await IXC.api('/api/multistream/check', { id }); if (!r.ok) toast(r.error, true); load(); }
      else if (a === 'relayStart' || a === 'relayStop') { const r = await IXC.api('/api/multistream/relay', { action: a === 'relayStart' ? 'start' : 'stop' }); if (!r.ok) toast(r.error, true); else toast(a === 'relayStart' ? 'Relay started' : 'Relay stopped'); load(); }
      else if (a === 'capsRefresh') { t.textContent = 'Asking OBS…'; await Caps.get(true); paint(); }
      else if (a === 'showkey') { showIngest = !showIngest; paint(); }
      else if (a === 'copy') { try { await navigator.clipboard.writeText(t.dataset.v); toast('Copied'); } catch (err) { toast('Select the text and press Ctrl+C', true); } } };
    main.onchange = async (e) => { const id = e.target.dataset.en; if (!id) return; const r = await IXC.api('/api/multistream/save', { id, enabled: e.target.checked }); if (!r.ok) toast(r.error, true); load(); };
    load(); const iv = setInterval(() => { if (App.current !== 'multistream-legacy') return clearInterval(iv); load(); }, 2000);
  });
  // ======================= VERTICAL STUDIO =======================
  // Honest shell: shows what OBS and its plugins really report. A 9:16 canvas with its own scenes and output needs an OBS-side component;
  // IXC controls for it are not built yet, so none are shown.
  page('vertical-legacy', 'Vertical Studio (old)', (main) => {
    async function paint() { if (App.current !== 'vertical-legacy') return; const c = Caps.data;
      const vv = c && c.ok ? Caps.vendor('aitum-vertical-canvas') : null; const det = !!(vv && vv.registered);
      main.innerHTML = card('📐 Vertical canvas (9:16)', (!c ? '<p class="muted">Asking OBS…</p>' : !c.ok ? Caps.down(c, 'id="vRefresh"') : `<div class="row" style="gap:10px;flex-wrap:wrap">${UI.pill(det ? 'connected' : 'off', det ? 'Aitum Vertical Canvas detected' + (vv.answer && vv.answer.version ? ' (' + esc(vv.answer.version) + ')' : '') : 'No vertical canvas component detected in OBS')}</div>
          <table class="t" style="margin-top:10px"><tr><td>OBS</td><td class="small">${esc(c.obs.obsVersion)} · obs-websocket ${esc(c.obs.obsWebSocketVersion)}</td></tr>
          <tr><td>Multiple canvases</td><td class="small">${c.hasCanvasRequests ? 'OBS reports canvas support (it can list canvases)' : 'OBS does not report canvas support'}</td></tr>
          <tr><td>Main canvas</td><td class="small">${c.videoSettings.baseWidth}×${c.videoSettings.baseHeight} base, ${c.videoSettings.outputWidth}×${c.videoSettings.outputHeight} output, ${c.videoSettings.fpsNumerator / c.videoSettings.fpsDenominator} fps (landscape)</td></tr>
          <tr><td>Vertical output</td><td class="small">${det ? 'A vertical component is present in OBS; IXC does not read or control its output yet.' : 'None - there is no vertical output to show.'}</td></tr></table>
          <div class="actions"><button class="small" id="vRefresh">Refresh</button> <span class="small muted">${esc(c.generatedAt)} · read-only</span></div>`)) +
        '<div style="height:14px"></div>' + card('What is and is not built', `<table class="t">
          <tr><td>See whether OBS and a vertical plugin are present</td><td><span class="pill st-connected"><i></i>Working (above)</span></td></tr>
          <tr><td>Start / stop the vertical stream, switch its scene, show its status</td><td>${NOTIMPL('needs a vertical component in OBS and a tested control path')}</td></tr>
          <tr><td>Portrait resolution, frame rate, destinations, layouts, presets</td><td>${NOTIMPL('set in OBS; IXC cannot edit them')}</td></tr>
          <tr><td>9:16 preview and scene/source composition inside IXC</td><td>${NOTIMPL('OBS does not offer a way to edit another canvas from outside')}</td></tr></table>
          <div class="hint" style="margin-top:8px">A real vertical output is a second canvas with its own scenes and its own encoder, rendered by OBS. IXC will only show controls here after they have been tested on this PC's OBS. A resized preview is not vertical support.</div>`);
    }
    main.onclick = async (e) => { if (e.target.id === 'vRefresh') { e.target.textContent = 'Asking OBS…'; await Caps.get(true); paint(); } };
    paint(); Caps.get().then(paint); const iv = setInterval(() => { if (App.current !== 'vertical-legacy') return clearInterval(iv); Caps.get().then(paint); }, 15000);
  });

  // ======================= AUTOMATION STUDIO =======================
  // Honest shell: shows what already runs automatically in IXC (real settings) and links to where each part is edited.
  page('automation', 'Automation Studio', (main) => {
    const V = () => S.values || {}; const cmds = () => (V()['commands.list'] || []), tms = () => (V()['timers.list'] || []);
    const EV = [['follow', 'New follower'], ['sub', 'New subscriber'], ['gift', 'Gifted subs'], ['raid', 'Raid / host'], ['superchat', 'Super Chat / Rant'], ['member', 'New YouTube member']];
    function paint() { if (App.current !== 'automation') return; const c = cmds(), t = tms(), evOn = EV.filter(([k]) => V()['events.' + k + '.on']);
      const row = (name, state, link, label) => '<tr><td><b>' + name + '</b><div class="small muted">' + state + '</div></td><td style="text-align:right"><button class="small" data-go="' + link + '">' + label + '</button></td></tr>';
      main.innerHTML = card('🤖 What runs automatically in IXC today', '<div class="hint" style="margin-bottom:8px">All of this is built into IXC - no Streamer.bot or other program is needed. These are the real settings of this IXC.</div><table class="t">' +
        row('Chat commands', c.length ? c.filter(x => x.enabled !== false).length + ' of ' + c.length + ' custom commands on, plus the built-in ones' : 'no custom commands yet; the built-in ones work', 'commands', 'Open Commands') +
        row('Timers', t.length ? t.filter(x => x.enabled !== false).length + ' of ' + t.length + ' timers on' + (V()['timers.enabled'] === false ? ' (timers switched off)' : '') : 'no timers yet', 'timers', 'Open Timers') +
        row('Event messages', evOn.length ? 'on for: ' + evOn.map(([, n]) => n).join(', ') : 'all switched off', 'commands', 'Open event messages') +
        row('Chat filters', 'link, caps, banned-word and duplicate filters', 'filters', 'Open Chat filters') +
        row('Chat voice and music', 'chat voice (TTS) and the music queue react to chat', 'tts', 'Open Chat voice') +
        row('OBS actions', 'a command can switch a scene or show / hide a source', 'obs', 'Open OBS Control') + '</table>') +
        '<div style="height:14px"></div>' + card('Automation Studio - what is not built yet', `<table class="t">
          <tr><td>Triggers beyond commands, timers and event messages (OBS events, going live, hotkeys)</td><td>${NOTIMPL('')}</td></tr>
          <tr><td>Actions made of several steps, with conditions and branches</td><td>${NOTIMPL('')}</td></tr>
          <tr><td>Variables, counters and saved state</td><td>${NOTIMPL('only a few fixed placeholders such as {uptime} {viewers} {song} exist')}</td></tr>
          <tr><td>Run history and a Test / Run button for each action</td><td>${NOTIMPL('')}</td></tr>
          <tr><td>Import / export of automations</td><td>${NOTIMPL('only a name-only import of Streamer.bot commands exists, in Commands')}</td></tr></table>
          <div class="hint" style="margin-top:8px">These will be added one by one, each with tests, on top of the engines above, which keep working as they are. IXC does not use or copy Streamer.bot.</div>`); }
    main.onclick = (e) => { const b = e.target.closest('[data-go]'); if (b) { e.preventDefault(); location.hash = '#' + b.dataset.go; } };
    async function load() { const d = await IXC.api('/api/settings'); if (d && d.values) { S.values = d.values; } paint(); }
    paint(); load(); const iv = setInterval(() => { if (App.current !== 'automation') return clearInterval(iv); load(); }, 4000);
  });
  // ======================= STREAMER.BOT (managed backend) =======================
  // IXC talks to Streamer.bot's local WebSocket API. The sign-ins to Twitch / YouTube / Kick live inside Streamer.bot (one time, in its own
  // window): IXC cannot do them for you. Status is drawn separately from the form so a refresh never wipes what is being typed.
  page('streamerbot', 'Streamer.bot', (main) => {
    main.innerHTML = `<p class="muted" style="margin-top:0">IXC can use <b>Streamer.bot</b> as a background helper for chat, replies and actions on Twitch, YouTube and Kick, so you don't have to register developer apps in IXC. IXC only talks to it on this PC (localhost).</p>
      <div class="grid"><div class="card" id="sbStatus"></div><div class="card" id="sbForm"></div></div>`;
    let d = null, formDone = false;
    const ago = (t) => { if (!t) return 'never'; const s = Math.max(0, Math.round((Date.now() - t) / 1000)); return s < 60 ? s + ' s ago' : s < 3600 ? Math.round(s / 60) + ' min ago' : Math.round(s / 3600) + ' h ago'; };
    const stateOf = (x) => { const l = x.link || {}; return l.ready ? ['connected', 'Connected'] : !x.installed ? ['off', 'Not found'] : !x.running ? ['off', 'Not running'] : ['reconnecting', l.detail || 'Waiting for Streamer.bot']; };
    function paintStatus() { const x = d, [st, label] = stateOf(x), l = x.link || {}, s = x.settings;
      const warn = [];
      if (s && !s.serverOn) warn.push('Streamer.bot\'s WebSocket Server is switched off (in Streamer.bot: Servers/Clients > WebSocket Server > Auto Start).');
      if (s && !s.localOnly) warn.push('Streamer.bot\'s WebSocket Server listens on ' + esc(s.address) + ', not only on this PC. Set its address to 127.0.0.1.');
      if (s && !s.authEnabled) warn.push('Streamer.bot\'s WebSocket password is switched off. Turn on Authentication there.');
      const tb = $('sbTest') ? $('sbTest').outerHTML : '<div class="small" id="sbTest" role="status" style="margin-top:8px"></div>';   // keep the last test result across refreshes
      $('sbStatus').innerHTML = `<h3>Status ${UI.pill(st, label)}</h3>
        <div class="kv small"><div>Program</div><div>${x.installed ? esc(x.path || '') : '<span class="warn">Streamer.bot.exe not found - choose its folder under Settings.</span>'}</div>
          <div>Version</div><div>${esc(x.version || '-')}</div>
          <div>Running</div><div>${x.running ? 'Yes' + (x.managed ? ' (started by IXC, pid ' + x.managedPid + ')' : ' (not started by IXC, IXC will not stop it)') : 'No'}</div>
          <div>Connection</div><div>${esc(l.ready ? 'Connected' + (l.auth === 'ok' ? ', signed in with the password' : l.auth === 'not required' ? ', no password needed' : '') : (l.detail || (x.mode === 'off' ? 'Turned off' : 'Not connected')))}</div>
          <div>Last event</div><div>${l.lastEvent ? ago(l.lastEvent) + ' (' + l.events + ' received)' : 'none yet'}</div>
          <div>Accounts</div><div>${(l.accounts || []).length ? esc(l.accounts.join(', ')) : '<span class="muted">none reported yet</span>'}</div>
          <div>Password</div><div>${x.passwordSet ? 'Set (' + esc(x.passwordSource) + ')' : '<span class="warn">none</span>'}</div></div>
        ${warn.map(w => `<div class="banner" style="margin-top:8px">${w}</div>`).join('')}
        ${x.startError ? `<div class="err small" style="margin-top:8px">${esc(x.startError)}</div>` : ''}
        <div class="actions" style="margin-top:10px"><button class="primary" data-sb="test">Test connection</button><button data-sb="start" ${x.running || !x.installed ? 'disabled' : ''}>Start Streamer.bot</button><button data-sb="stop" ${x.managed ? '' : 'disabled'} title="${x.managed ? '' : 'Only a Streamer.bot that IXC started can be stopped here'}">Stop</button></div>
        ${tb}
        <div class="hint" style="margin-top:8px"><b>Signing in to Twitch, YouTube and Kick</b> is done once inside Streamer.bot's own window (Platforms tab). IXC cannot do it for you and never sees those sign-ins. After that IXC runs it in the background.</div>`; }
    function paintForm() { if (formDone) return; formDone = true;
      $('sbForm').innerHTML = `<h3>Settings</h3>
        <div class="field"><label class="lbl">Use Streamer.bot</label><div class="val"><select id="sbMode"><option value="auto">Automatically, when it is running</option><option value="on">Always (keep trying)</option><option value="off">Never</option></select></div></div>
        <div class="field"><label class="lbl">Address</label><div class="val"><input id="sbUrl" autocomplete="off" spellcheck="false" placeholder="ws://127.0.0.1:8080/"></div></div>
        <div class="field"><label class="lbl">Password</label><div class="val"><input id="sbPw" type="password" autocomplete="new-password"> <span class="ok small" id="sbPwSaved" hidden>🔒 Saved</span></div></div>
        <div class="tiny muted">The password is stored encrypted on this PC and never shown again. Leave it empty and IXC looks it up in Streamer.bot's own settings file.</div>
        <div class="field"><label class="lbl">Program</label><div class="val"><input id="sbExe" autocomplete="off" spellcheck="false" placeholder="folder with Streamer.bot.exe (found automatically)"></div></div>
        <div class="field"><label class="lbl">Start with IXC</label><div class="val"><select id="sbLaunch"><option value="never">No, I start it myself</option><option value="withIxc">Yes, keep it running in the background</option></select></div></div><div class="field"><label class="lbl">When IXC quits<div class="hint">only a Streamer.bot that IXC started; never while OBS is live, never forced</div></label><div class="val"><select id="sbOnExit"><option value="keep">Leave Streamer.bot running</option><option value="close">Close Streamer.bot</option></select></div></div>
        <div class="actions"><button class="primary" data-sb="save">Save</button></div><div class="small" id="sbMsg" role="status" style="margin-top:6px"></div>`;
      fillForm(); }
    function fillForm() { const x = d, set = (id, v) => { const e = $(id); if (e) e.value = v; };
      set('sbMode', x.mode); set('sbUrl', x.url); set('sbExe', x.exePath || ''); set('sbLaunch', x.launch); set('sbOnExit', x.onExit || 'keep'); $('sbPw').value = ''; $('sbPw').placeholder = x.passwordSet ? '(set - type a new one to replace it)' : ''; $('sbPwSaved').hidden = !x.passwordSet; }
    const msg = (id, text, bad) => { const e = $(id); if (e) { e.className = 'small ' + (bad ? 'err' : 'ok'); e.textContent = text; } };
    async function load() { const r = await IXC.api('/api/streamerbot'); if (r && r.mode !== undefined) { d = r; paintStatus(); paintForm(); } else if (!d) $('sbStatus').innerHTML = '<div class="err">IXC did not answer.</div>'; }
    main.onclick = async (e) => { const b = e.target.closest('[data-sb]'); if (!b) return; const a = b.dataset.sb; b.disabled = true;
      try {
        if (a === 'test') { msg('sbTest', 'Testing...', false); const r = await IXC.api('/api/streamerbot/test', {}); const text = r.ok ? 'Connected' + (r.version ? ' to Streamer.bot ' + r.version : '') + (r.authRequired ? ', password accepted' : ', no password needed') + (r.accounts && r.accounts.length ? '. Accounts: ' + r.accounts.join(', ') : '. No platform account reported - sign in inside Streamer.bot.') : r.error || 'Failed'; msg('sbTest', text, !r.ok); }
        else if (a === 'start') { const r = await IXC.api('/api/streamerbot/start', {}); toast(r.ok ? 'Starting Streamer.bot...' : r.error, !r.ok); }
        else if (a === 'stop') { let r = await IXC.api('/api/streamerbot/stop', {}); if (!r.ok && /streaming or recording/.test(r.error || '')) { if (await UI.confirmBox(r.error, 'Stop anyway')) r = await IXC.api('/api/streamerbot/stop', { confirmLive: true }); } if (!r.ok && /did not close/.test(r.error || '')) { if (await UI.confirmBox(r.error, 'Force stop')) r = await IXC.api('/api/streamerbot/stop', { force: true, confirmLive: true }); } if (!r.ok) toast(r.error, true); }
        else if (a === 'save') {
          const body = { mode: $('sbMode').value, url: $('sbUrl').value.trim(), exePath: $('sbExe').value.trim(), launch: $('sbLaunch').value, onExit: $('sbOnExit').value }; const pw = $('sbPw').value; if (pw) body.password = pw;
          const r = await IXC.api('/api/streamerbot/config', body);
          if (!r || !r.ok) { const why = (r && r.error) || 'IXC did not answer - is it running?'; msg('sbMsg', 'Not saved: ' + why + ' Your entries are still here.', true); return; }   // entries stay as typed
          const f = await IXC.api('/api/streamerbot');   // confirm from the backend, not from the browser
          if (!f || f.url !== body.url || f.mode !== body.mode || f.launch !== body.launch || (pw && !f.passwordSet)) { msg('sbMsg', 'IXC said "saved" but reading the settings back did not show them. Your entries are still here - try again.', true); return; }
          d = f; fillForm(); paintStatus(); msg('sbMsg', 'Saved.', false); }
      } finally { b.disabled = false; load(); } };
    load(); const iv = setInterval(() => { if (App.current !== 'streamerbot') return clearInterval(iv); load(); }, 3000);
  });
})();
