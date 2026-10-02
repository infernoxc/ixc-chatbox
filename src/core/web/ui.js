// IXC UI helpers shared by the dashboard, the OBS panels and overlays. Part of IXC - (c) 2026 Ishan (InFerNoxC) - MIT License
window.UI = (() => {
  const $ = (id) => document.getElementById(id);
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const P = { twitch: { name: 'Twitch', tag: 'TWITCH', color: '#9146ff' }, kick: { name: 'Kick', tag: 'KICK', color: '#53fc18' }, youtube: { name: 'YouTube', tag: 'YT', color: '#ff0033' }, rumble: { name: 'Rumble', tag: 'RUMBLE', color: '#85c742' }, test: { name: 'Test', tag: 'TEST', color: '#777' } };
  const STATE = { connected: 'Connected', connecting: 'Connecting…', reconnecting: 'Reconnecting…', auth: 'Sign-in required', ratelimited: 'Rate limited', unavailable: 'Not live', off: 'Off', error: 'Error', live: 'Live', offline: 'Offline', stale: 'No recent data', waiting: 'Checking…' };
  let toastT;
  function toast(t, bad) { let el = $('toast'); if (!el) { el = document.createElement('div'); el.id = 'toast'; el.setAttribute('role', 'status'); document.body.appendChild(el); }
    el.textContent = t; el.classList.toggle('bad', !!bad); el.classList.add('show'); clearTimeout(toastT); toastT = setTimeout(() => el.classList.remove('show'), bad ? 5000 : 2800); }
  const time = (s) => { s = Math.max(0, Math.floor(+s || 0)); const h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), x = s % 60; return (h ? h + ':' + String(m).padStart(2, '0') : m) + ':' + String(x).padStart(2, '0'); };
  const num = (n) => n == null ? '–' : Number(n).toLocaleString('en-US');
  const thumb = (it) => it && it.kind === 'yt' && it.id ? `https://i.ytimg.com/vi/${it.id}/mqdefault.jpg` : it && it.id && /^[\w-]{11}$/.test(it.id) ? `https://i.ytimg.com/vi/${it.id}/mqdefault.jpg` : '';
  const pill = (state, label) => `<span class="pill st-${esc(state)}"><i></i>${esc(label ?? STATE[state] ?? state)}</span>`;
  // a chat message as HTML (text is always escaped; emote images only from https)
  function msgHtml(m) {
    const parts = (m.parts && m.parts.length ? m.parts : [{ t: 'text', v: m.text || '' }]).map(p => p.t === 'emote' && /^https:\/\//.test(p.url || '') ? `<img class="em" src="${esc(p.url)}" alt="${esc(p.name)}" title="${esc(p.name)}" loading="lazy" onerror="this.replaceWith(document.createTextNode(this.alt))">` : esc(p.v ?? p.name ?? '')).join('');
    return parts; }
  function qr(el, text) { el.innerHTML = ''; if (!text) return; try { const q = qrcode(0, 'M'); q.addData(text); q.make(); el.innerHTML = q.createSvgTag({ cellSize: 6, margin: 2, scalable: true }); } catch (e) { el.textContent = 'QR code could not be drawn'; } }
  function confirmBox(text, okLabel) { return new Promise((res) => { const m = document.createElement('div'); m.className = 'modal show';
    m.innerHTML = `<div class="card" role="dialog" aria-modal="true"><p style="margin:0 0 14px">${esc(text)}</p><div class="row" style="justify-content:flex-end"><button class="ghost" data-a="0">Cancel</button><button class="primary" data-a="1">${esc(okLabel || 'OK')}</button></div></div>`;
    document.body.appendChild(m); m.querySelector('[data-a="1"]').focus(); m.onclick = (e) => { const a = e.target.dataset.a; if (a == null && e.target !== m) return; m.remove(); res(a === '1'); };
    m.onkeydown = (e) => { if (e.key === 'Escape') { m.remove(); res(false); } }; }); }
  return { $, esc, P, STATE, toast, time, num, thumb, pill, msgHtml, qr, confirmBox };
})();
