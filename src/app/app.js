// IXC dashboard - shell: live state, navigation and settings controls. Pages are in pages.js, the first-run wizard in wizard.js.
// Every control saves through IXC's settings API, which validates everything (nobody ever edits config files).
// Part of IXC - (c) 2026 Ishan (InFerNoxC) - MIT License
window.App = (() => {
  const { $, esc, toast } = UI;
  const S = { status: null, values: {}, schema: {}, voices: [], accounts: null, remote: null, music: null, route: null, tts: null, viewers: null, up: false, outputFolder: '', dataFolder: '' };
  const pages = {}; let current = null, listeners = [];
  const DOCK = new URLSearchParams(location.search).get('dock') === '1'; if (DOCK) document.body.classList.add('dock');
  const NAV = [['home', '🏠', 'Home'], ['', '', 'Live'], ['chat', '💬', 'Chat'], ['music', '🎵', 'Music'], ['requests', '🙋', 'Song requests'], ['', '', 'Setup'], ['accounts', '🔗', 'Platforms & accounts'], ['overlays', '🖼️', 'Overlays'], ['phone', '📱', 'Phone remote'],
    ['', '', 'Chat tools'], ['tts', '🔊', 'Chat voice (TTS)'], ['voices', '🎙️', 'Voices for people'], ['commands', '⌨️', 'Commands'], ['filters', '🛡️', 'Chat filters'], ['', '', 'System'], ['check', '🩺', 'System check'], ['settings', '⚙️', 'Settings']];
  function navHtml() { $('navList').innerHTML = NAV.map(([id, ic, t]) => id ? `<a href="#${id}" data-p="${id}"><span class="ic" aria-hidden="true">${ic}</span>${esc(t)}<span class="badge" hidden></span></a>` : `<div class="grp">${esc(t)}</div>`).join(''); }
  // ---------- live state ----------
  const link = IXC.connect({ role: 'app', topics: ['status', 'music.state', 'tts', 'remote', 'accounts', 'viewers', 'settings', 'reload'], onState, on: onMsg });
  function onState(up) { S.up = up; $('connPill').className = 'pill st-' + (up ? 'connected' : 'reconnecting'); $('connPill').innerHTML = '<i></i>' + (up ? 'IXC running' : 'IXC not running - reconnecting'); if (up) reload(); }
  async function reload() { const d = await IXC.api('/api/settings'); if (d.values) { S.values = d.values; S.voices = d.voices || []; S.outputFolder = d.outputFolder; S.dataFolder = d.dataFolder; S.schema = {}; (d.schema || []).forEach(x => S.schema[x.key] = x); }
    S.accounts = await IXC.api('/api/accounts'); emit('settings'); emit('accounts'); if (!current) route(); }
  function onMsg(m) {
    switch (m.type) {
      case 'status': S.status = m; $('ver').textContent = 'version ' + m.version; paintHealth(); emit('status'); if (m.firstRun && !Wizard.shown && !DOCK) Wizard.open(); break;
      case 'music.state': S.music = m.s; S.route = m.route; emit('music'); break;
      case 'tts.state': S.tts = m; emit('tts'); break;
      case 'tts.voices': S.voices = m.voices || S.voices; break;
      case 'remote.state': S.remote = m; emit('remote'); break;
      case 'remote.paired': emit('paired', m); break;
      case 'accounts': S.accounts = m.accounts; emit('accounts'); break;
      case 'viewers': S.viewers = m; emit('viewers'); break;
      case 'settings': S.values[m.key] = m.value; emit('settings'); break;
      case 'settings.all': break;
      case 'error': toast(m.error, true); break;
    } }
  function paintHealth() { const st = S.status; if (!st) return; const n = (st.issues || []).length;
    $('healthPill').innerHTML = `<a href="#check" style="text-decoration:none">${UI.pill(st.overall === 'healthy' ? 'healthy' : st.overall === 'error' ? 'error' : 'warning', st.overall === 'healthy' ? 'All good' : n + ' thing' + (n === 1 ? '' : 's') + ' to check')}</a>`;
    const b = document.querySelector('#navList a[data-p="check"] .badge'); if (b) b.hidden = n === 0; }
  function on(ev, fn) { listeners.push([ev, fn]); }
  function emit(ev, d) { listeners.filter(l => l[0] === ev).forEach(l => { try { l[1](d); } catch (e) { console.error(e); } }); }
  // ---------- navigation ----------
  function page(id, title, render) { pages[id] = { title, render }; }
  function route() {
    const h = (location.hash || '#home').slice(1); const [id, qs] = h.split('?'); const p = pages[id] || pages.home; current = id; listeners = [];
    document.querySelectorAll('#navList a').forEach(a => a.classList.toggle('sel', a.dataset.p === id)); $('pageTitle').textContent = p.title; document.title = p.title + ' - IXC';
    $('nav').classList.remove('open'); const main = $('main'); main.innerHTML = ''; main.scrollTop = 0;
    try { p.render(main, new URLSearchParams(qs || '')); } catch (e) { console.error(e); main.innerHTML = '<div class="banner err">This page had a problem: ' + esc(e.message) + '</div>'; }
    paintHealth(); }
  window.addEventListener('hashchange', route);
  $('menuBtn').onclick = () => $('nav').classList.toggle('open');
  // ---------- settings controls (one line each) ----------
  const val = (k) => S.values[k];
  async function save(patch, quiet) { const r = await IXC.api('/api/settings', { patch }); if (r.ok) { S.values = r.values; if (!quiet) toast('Saved'); emit('settings'); } else toast(r.error || 'Could not save', true); return r.ok; }
  function lbl(label, hint) { return `<label class="lbl">${esc(label)}${hint ? `<div class="hint">${hint}</div>` : ''}</label>`; }
  const ctl = {
    toggle: (k, label, hint) => `<div class="field">${lbl(label, hint)}<div class="val"><label class="switch"><input type="checkbox" data-k="${k}" data-kind="bool" ${val(k) ? 'checked' : ''} aria-label="${esc(label)}"><i></i></label></div></div>`,
    range: (k, label, hint, fmt) => { const s = S.schema[k] || {}; const v = val(k); return `<div class="field">${lbl(label, hint)}<div class="val"><input type="range" data-k="${k}" data-kind="int" data-fmt="${fmt || ''}" min="${s.min}" max="${s.max}" step="${s.max - s.min > 300 ? 10 : 1}" value="${v}" aria-label="${esc(label)}"><output>${fmtV(v, fmt)}</output></div></div>`; },
    select: (k, label, opts, hint) => `<div class="field">${lbl(label, hint)}<div class="val"><select data-k="${k}" data-kind="str" aria-label="${esc(label)}">${opts.map(([v, t]) => `<option value="${esc(v)}" ${String(val(k)) === String(v) ? 'selected' : ''}>${esc(t)}</option>`).join('')}</select></div></div>`,
    text: (k, label, ph, hint) => `<div class="field">${lbl(label, hint)}<div class="val"><input type="text" data-k="${k}" data-kind="str" value="${esc(val(k) ?? '')}" placeholder="${esc(ph || '')}" aria-label="${esc(label)}"></div></div>`,
    list: (k, label, hint, ph) => `<div class="field">${lbl(label, hint)}<div class="val"><textarea data-k="${k}" data-kind="list" placeholder="${esc(ph || 'one per line')}" aria-label="${esc(label)}">${esc((val(k) || []).join('\n'))}</textarea></div></div>`,
    checks: (k, label, opts, hint) => `<div class="field">${lbl(label, hint)}<div class="val chipset" data-k="${k}" data-kind="checks">${opts.map(([v, t]) => `<label><input type="checkbox" value="${esc(v)}" ${(val(k) || []).includes(v) ? 'checked' : ''}>${esc(t)}</label>`).join('')}</div></div>`,
  };
  const ROLE_OPTS = [['everyone', 'Everyone'], ['follower', 'Followers and up'], ['subscriber', 'Subscribers / members and up'], ['vip', 'VIPs and up'], ['moderator', 'Moderators and up'], ['broadcaster', 'Only me']];
  const PLAT_OPTS = [['twitch', 'Twitch'], ['kick', 'Kick'], ['youtube', 'YouTube'], ['rumble', 'Rumble']];
  function fmtV(v, f) { if (f === '%') return v + '%'; if (f === 's') return v + ' s'; if (f === 'ms') return (v / 1000).toFixed(1) + ' s'; if (f === 'x') return (1 + v / 100).toFixed(2) + '×'; if (f === 'mmss') return v ? UI.time(v) : 'no limit'; if (f === 'd') return v ? v + ' days' : 'any'; return v; }
  // one delegated handler saves every control
  document.addEventListener('change', (e) => { const el = e.target; const box = el.closest('[data-k]'); if (!box || !box.closest('#main, #wzCard, #modalCard')) return; const k = box.dataset.k, kind = box.dataset.kind;
    let v; if (kind === 'bool') v = el.checked; else if (kind === 'int') v = +el.value; else if (kind === 'list') v = el.value.split(/\n|,/).map(x => x.trim()).filter(Boolean); else if (kind === 'checks') v = [...box.querySelectorAll('input:checked')].map(x => x.value); else v = el.value;
    save({ [k]: v }); });
  document.addEventListener('input', (e) => { const el = e.target; if (el.type === 'range' && el.dataset.k) { const o = el.parentElement.querySelector('output'); if (o) o.textContent = fmtV(+el.value, el.dataset.fmt); } });
  // ---------- modal ----------
  function modal(html) { $('modalCard').innerHTML = html; $('modal').classList.add('show'); const f = $('modalCard').querySelector('input,button,select,textarea'); if (f) f.focus(); }
  function closeModal() { $('modal').classList.remove('show'); $('modalCard').innerHTML = ''; emit('modalClosed'); }
  $('modal').addEventListener('click', (e) => { if (e.target === $('modal') || e.target.closest('[data-close]')) closeModal(); });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && $('modal').classList.contains('show')) closeModal(); });
  async function repair(action, btn) { if (btn) { btn.disabled = true; btn.dataset.t = btn.innerHTML; btn.innerHTML = '<span class="spin"></span> Working…'; }
    if (action.startsWith('page:')) { location.hash = '#' + action.slice(5); return; }
    const r = await IXC.api('/api/repair', { action }); if (btn) { btn.disabled = false; btn.innerHTML = btn.dataset.t; }
    if (r.page) { location.hash = '#' + (r.page === 'backups' ? 'check' : r.page); return r; }
    toast(r.ok ? r.message : r.error, !r.ok); return r; }
  navHtml();
  return { S, ctl, save, page, route, on, link, modal, closeModal, repair, ROLE_OPTS, PLAT_OPTS, fmtV, DOCK, get current() { return current; } };
})();
