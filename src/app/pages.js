// IXC dashboard - pages. Part of IXC - (c) 2026 Ishan (InFerNoxC) - MIT License
(() => {
  const { $, esc, toast, P } = UI; const { S, ctl, page, on, repair } = App;
  const card = (title, body, cls) => `<div class="card ${cls || ''}"><h3>${title}</h3>${body}</div>`;
  const plats = ['twitch', 'kick', 'youtube', 'rumble'];

  // ======================= HOME =======================
  page('home', 'Home', (main) => {
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
      `<div style="height:14px"></div>` + card('📁 Your own music files', `<div id="folders" class="list"></div><div class="actions"><button id="addFolder" class="primary">+ Add a music folder</button></div><div class="hint" style="margin-top:8px">MP3, M4A, AAC, OGG, OPUS, WAV and FLAC files in the folder and its subfolders can be searched and played like YouTube songs.</div>`) +
      `<div style="height:14px"></div>` + card('🎯 Which platform hears the music', `<div class="hint">The "Music goes to" buttons switch which OBS audio tracks carry the music. Tell IXC which track each platform uses (how your OBS output or multistream plugin is set up). Most people with one platform can ignore this.</div>` +
        ['twitch', 'kick', 'youtube'].map(p => ctl.checks('music.routing.tracks.' + p, P[p].name + ' uses track', [1, 2, 3, 4, 5, 6].map(n => [String(n), String(n)]))).join('')) +
      `<div style="height:14px"></div>` + card('🏷 Now Playing text', ctl.text('nowPlaying.textFormat', 'Text format', '{title} - {artist}', 'use {title}, {artist}, {by}') + ctl.toggle('nowPlaying.writeFile', 'Write nowplaying.txt', 'for OBS Text sources and other tools') +
        `<div class="hint">File: <code>${esc(S.outputFolder)}\\nowplaying.txt</code> · Web: <code>${location.origin}/api/nowplaying.txt</code> · JSON: <code>${location.origin}/api/nowplaying</code></div><div class="actions"><button data-open="output">Open the folder</button></div>`) +
      (S.values['general.advanced'] ? `<div style="height:14px"></div>` + card('🔧 Advanced', ctl.text('music.youtubeApiKey', 'YouTube Data API key', 'optional', 'more reliable search (100 searches/day free)') + ctl.text('music.spotify.clientId', 'Spotify app Client ID', 'optional', 'for Spotify playlists and albums') +
        `<div class="field"><label class="lbl">Spotify Client Secret<div class="hint">stored encrypted, never shown again</div></label><div class="val"><input type="password" id="spSecret" placeholder="paste to set or replace" autocomplete="off"><button id="spSave">Save</button></div></div>` + ctl.text('music.routing.inputName', 'Music source name in OBS', 'IXC Music Player')) : '');
      $('duckTest').onclick = () => App.link.send({ type: 'tts.test' });
      $('addFolder').onclick = async (e) => { e.target.disabled = true; e.target.textContent = 'A folder window opened on your PC…'; const r = await IXC.api('/api/music/folders', {}); e.target.disabled = false; e.target.textContent = '+ Add a music folder'; if (r.ok) { toast('Folder added - scanning…'); App.S.values['music.localFolders'] = r.folders; folders(); } else toast(r.error, true); };
      document.querySelectorAll('[data-open]').forEach(b => b.onclick = () => IXC.api('/api/open', { what: b.dataset.open }));
      const sp = $('spSave'); if (sp) sp.onclick = async () => { const r = await IXC.api('/api/music/spotify-secret', { secret: $('spSecret').value }); $('spSecret').value = ''; toast(r.ok ? 'Spotify secret saved' : r.error, !r.ok); };
      folders(); };
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
  page('accounts', 'Platforms & accounts', (main) => {
    main.innerHTML = `<p class="muted" style="margin-top:0">Reading chat needs only your channel name. <b>Connect</b> your account to also reply from IXC, get follower alerts and see Twitch viewers.</p><div class="grid" id="acc"></div>`;
    const NOTE = { twitch: 'Reads chat without signing in. Sign in for replies, follower alerts and viewer counts.', kick: 'Reads chat and viewers without signing in. Sign in to reply.', youtube: 'Finds your live stream by itself. Sign in to reply in YouTube chat.', rumble: 'Copy your "Live Stream API" link from rumble.com (Account > Live Stream API) and paste it here. Rumble doesn\'t allow apps to send chat.' };
    const paint = () => { const st = (S.status || {}).chat || {}, acc = S.accounts || {};
      $('acc').innerHTML = plats.map(p => { const s = st[p] || {}, a = acc[p] || {}, ac = a.account, fl = a.flow;
        const chan = p === 'rumble' ? `<div class="field"><label class="lbl">Live Stream API link</label><div class="val"><input type="url" id="rumbleUrl" placeholder="${ac ? '(saved - paste a new one to replace it)' : 'https://rumble.com/-livestream-api/get-data?key=…'}"><button id="rumbleSave">Save</button></div></div><div class="actions"><button data-open-url="https://rumble.com/account/livestream-api">Open rumble.com to copy it</button></div>`
          : ctl.text('platforms.' + p + '.channel', p === 'youtube' ? 'Your channel' : 'Your channel name', p === 'youtube' ? '@yourhandle or channel link' : p === 'twitch' ? 'yourname' : 'your-name');
        const account = p === 'rumble' ? '' : !a.available ? `<div class="hint">Signing in isn't available in this build.</div>` : ac ? `<div class="row" style="margin-top:6px"><span class="grow">Signed in as <b>${esc(ac.displayName || ac.login)}</b>${ac.invalid ? ' <span class="warn">(expired)</span>' : ''}</span>${ac.invalid ? `<button class="primary" data-connect="${p}">Sign in again</button>` : ''}<button data-signout="${p}">Sign out</button></div>`
          : fl && fl.state === 'waiting' ? `<div class="banner info" style="margin-top:6px">${esc(fl.message)}${fl.userCode ? ` - code <b style="font-size:18px;letter-spacing:2px">${esc(fl.userCode)}</b>` : ''}<span class="grow"></span>${fl.url ? `<button data-open-url="${esc(fl.url)}">Open again</button>` : ''}<button data-cancel="${p}">Cancel</button></div>`
          : `<div class="actions"><button class="primary" data-connect="${p}">Connect ${P[p].name} account</button></div>${fl && fl.state === 'failed' ? `<div class="err small">${esc(fl.message)}</div>` : ''}`;
        return `<div class="card"><div class="plat"><span class="logo2 ${p}">${P[p].tag.slice(0, 2)}</span><div class="grow"><h3 style="margin:0">${P[p].name}</h3><div class="small muted">${esc(s.configured ? s.detail || '' : '')}</div></div>${UI.pill(s.configured ? s.state : 'off')}</div>
          ${ctl.toggle('platforms.' + p + '.enabled', 'Use ' + P[p].name)}${chan}${account}<div class="hint" style="margin-top:8px">${NOTE[p]}${s.configured && s.sendVia === 'streamerbot' ? '<br>Replies: sent through Streamer.bot ✓' : s.configured && !s.canSend && p !== 'rumble' ? '<br>Replies: ' + esc(s.sendNote) + ' - or run Streamer.bot (connected to ' + P[p].name + ') to reply through it' : ''}</div></div>`; }).join('') +
        `<div class="card"><div class="plat"><span class="logo2 streamerbot">SB</span><div class="grow"><h3 style="margin:0">Streamer.bot <span class="tiny muted">(optional)</span></h3><div class="small muted">${esc((st.streamerbot || {}).detail || '')}</div></div>${UI.pill((st.streamerbot || {}).state || 'off')}</div>
          ${ctl.select('platforms.streamerbot.mode', 'Use Streamer.bot', [['auto', 'Automatically, when it\'s running'], ['on', 'Always (keep trying)'], ['off', 'Never']])}
          <div class="hint">You don't need Streamer.bot. If you already use it, IXC reads its chat and can reply through it for platforms you haven't connected here.</div>${S.values['general.advanced'] ? ctl.text('platforms.streamerbot.websocketUrl', 'WebSocket address', 'ws://127.0.0.1:8080/') : ''}</div>`;
      const rs = $('rumbleSave'); if (rs) rs.onclick = async () => { const r = await IXC.api('/api/accounts/rumble/rumble', { url: $('rumbleUrl').value }); toast(r.ok ? 'Rumble connected' : r.error, !r.ok); }; };
    main.onclick = async (e) => { const b = e.target.closest('button'); if (!b) return; const d = b.dataset;
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
  page('commands', 'Commands', (main) => {
    let list = [];
    async function load() { const d = await IXC.api('/api/settings'); S.values = d.values || S.values; list = (S.values['commands.list'] || []).map(x => Object.assign({}, x)); paint(); }
    const BUILT = { np: 'Says what\'s playing', queue: 'Says what\'s next', skip: 'Skips the song', uptime: 'How long you\'ve been live', wrongsong: 'Removes your last request', ttsskip: 'Skips the chat voice message', ttson: 'Turns the chat voice on', ttsoff: 'Turns the chat voice off', viewers: 'Says the viewer count', pause: 'Pauses music', resume: 'Resumes music', songs: 'Custom text' };
    function paint() { main.innerHTML = card('⌨️ Chat commands', ctl.toggle('commands.enabled', 'Commands on') + `<table class="t"><tr><th>On</th><th>Command</th><th>Answer</th><th>Who</th><th>Cooldown</th><th></th></tr>${list.map((c, i) => `<tr><td><label class="switch"><input type="checkbox" data-en="${i}" ${c.enabled ? 'checked' : ''} aria-label="On"><i></i></label></td>
        <td><b>${esc(c.trigger)}</b>${c.aliases && c.aliases.length ? '<div class="tiny muted">' + esc(c.aliases.join(' ')) + '</div>' : ''}</td><td class="small">${c.builtin ? '<span class="muted">' + esc(BUILT[c.builtin] || '') + '</span>' + (c.response ? ' · ' : '') : ''}${esc(c.response)}</td>
        <td class="small">${esc(c.permission)}</td><td class="small">${c.cooldownSec ? c.cooldownSec + ' s' : '-'}</td><td><button class="small" data-ed="${i}">Edit</button> <button class="small" data-rm="${i}">✕</button></td></tr>`).join('')}</table>
        <div class="actions"><button class="primary" id="cAdd">+ New command</button><button id="cReset">Restore default commands</button></div>
        <div class="hint" style="margin-top:10px">In answers you can use {user} {name} {target} {args} {song} {title} {artist} {queue} {uptime} {viewers} {count} {platform}. Answers need IXC to be able to reply on that platform (Platforms & accounts).</div>`); }
    async function store() { const r = await IXC.api('/api/settings', { patch: { 'commands.list': list } }); if (!r.ok) { toast(r.error, true); await load(); return false; } S.values = r.values; list = r.values['commands.list'].map(x => Object.assign({}, x)); paint(); toast('Saved'); return true; }
    function edit(i) { const c = i < 0 ? { trigger: '!', response: '', permission: 'everyone', cooldownSec: 10, userCooldownSec: 0, platforms: [], enabled: true, aliases: [] } : Object.assign({}, list[i]);
      App.modal(`<h3>${i < 0 ? 'New command' : 'Edit ' + esc(c.trigger)}</h3><div class="field"><label class="lbl">Command</label><div class="val"><input type="text" id="eT" value="${esc(c.trigger)}" ${c.builtin ? 'disabled' : ''}></div></div>
        <div class="field"><label class="lbl">Other names</label><div class="val"><input type="text" id="eA" value="${esc((c.aliases || []).join(' '))}" placeholder="!dc !disc"></div></div>
        <div class="field"><label class="lbl">Answer</label><div class="val"><textarea id="eR" style="font-family:inherit">${esc(c.response)}</textarea></div></div>
        <div class="field"><label class="lbl">Who can use it</label><div class="val"><select id="eP">${App.ROLE_OPTS.map(([v, t]) => `<option value="${v}" ${c.permission === v ? 'selected' : ''}>${esc(t)}</option>`).join('')}</select></div></div>
        <div class="field"><label class="lbl">Cooldown (s)</label><div class="val"><input type="number" id="eC" min="0" max="3600" value="${c.cooldownSec}"></div></div>
        <div class="field"><label class="lbl">Per person cooldown (s)</label><div class="val"><input type="number" id="eU" min="0" max="3600" value="${c.userCooldownSec}"></div></div>
        <div class="field"><label class="lbl">Platforms</label><div class="val chipset" id="ePl">${App.PLAT_OPTS.map(([v, t]) => `<label><input type="checkbox" value="${v}" ${!c.platforms.length || c.platforms.includes(v) ? 'checked' : ''}>${t}</label>`).join('')}</div></div>
        <div class="row" style="justify-content:flex-end;margin-top:14px"><button data-close>Cancel</button><button class="primary" id="eSave">Save</button></div>`);
      $('eSave').onclick = async () => { c.trigger = $('eT').value.trim(); c.aliases = $('eA').value.split(/[\s,]+/).filter(Boolean); c.response = $('eR').value.trim(); c.permission = $('eP').value; c.cooldownSec = +$('eC').value; c.userCooldownSec = +$('eU').value;
        const pl = [...document.querySelectorAll('#ePl input:checked')].map(x => x.value); c.platforms = pl.length === 4 ? [] : pl;
        if (i < 0) list.push(c); else list[i] = c; if (await store()) App.closeModal(); else if (i < 0) list.pop(); }; }
    main.onclick = async (e) => { const d = e.target.dataset; if (e.target.id === 'cAdd') edit(-1); if (d.ed) edit(+d.ed);
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
    function upd() { const u = (S.status || {}).update || {}; $('upd').innerHTML = `<div class="kv"><span>Installed</span><span>${esc(u.current || '')}</span><span>Latest</span><span>${esc(u.latest || 'not checked yet')}</span></div>${u.error ? `<div class="warn small" style="margin-top:6px">${esc(u.error)}</div>` : ''}${u.progress ? `<div class="small" style="margin-top:6px">${esc(u.progress)}</div>` : ''}
      <div class="actions"><button id="uChk">Check now</button>${u.available ? `<button class="primary" data-fix="update.install">Update to ${esc(u.latest)}</button>` : ''}</div>${u.available && u.notes ? `<details style="margin-top:8px"><summary>What's new</summary><div class="small" style="white-space:pre-wrap">${esc(u.notes)}</div></details>` : ''}`;
      $('uChk').onclick = async () => { const r = await IXC.api('/api/update/check'); S.status.update = r; upd(); toast(r.available ? 'An update is available' : r.error || 'IXC is up to date', !!r.error); }; }
    $('logArea').onchange = logs; backups(); logs(); upd(); on('status', upd); const lt = setInterval(() => { if (App.current !== 'check') return clearInterval(lt); logs(); }, 3000);
    if (location.hash.includes('run')) run();
  });

  // ======================= SETTINGS =======================
  page('settings', 'Settings', (main) => {
    const paint = () => { const adv = S.values['general.advanced'];
      main.innerHTML = `<div class="grid2"><div>` + card('⚙️ General', ctl.toggle('general.autoRestart', 'Restart IXC by itself if it stops unexpectedly') + ctl.toggle('general.checkUpdates', 'Check for updates') + ctl.toggle('general.openDashboardOnStart', 'Open this window when Windows starts') +
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
})();
