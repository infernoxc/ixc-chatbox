// IXC overlays - shared runtime for every on-stream browser source in /overlay/.
// * Style comes from IXC (dashboard > Overlays, changes apply live) and can be overridden per source with URL options.
// * The background is always transparent; when IXC is unreachable the overlay keeps showing its last state and reconnects.
// * A script error never leaves a frozen or white overlay: after repeated errors it reloads itself (at most once a minute).
// Part of IXC - (c) 2026 Ishan (InFerNoxC) - MIT License
window.Overlay = (() => {
  const q = new URLSearchParams(location.search); const PREVIEW = q.get('preview') === '1';
  let errs = [];
  window.addEventListener('error', () => { const now = Date.now(); errs = errs.filter(t => now - t < 60000); errs.push(now);
    try { const last = +sessionStorage.getItem('ixc-ov-reload') || 0; if (errs.length >= 5 && now - last > 60000) { sessionStorage.setItem('ixc-ov-reload', String(now)); location.reload(); } } catch {} });
  function coerce(v, d) { if (typeof d === 'boolean') return v === '1' || v === 'true' || v === true; if (typeof d === 'number') { const n = +v; return isNaN(n) ? d : n; } if (Array.isArray(d)) return Array.isArray(v) ? v : String(v).split(',').filter(Boolean); return String(v); }
  function start(o) {
    const name = o.name; let saved = {}, style = {};
    const compute = () => { style = Object.assign({}, o.defaults); for (const k in saved) if (k in o.defaults) style[k] = coerce(saved[k], o.defaults[k]);
      for (const [k, v] of q) if (k in o.defaults) style[k] = coerce(v, o.defaults[k]); applyBase(style); try { o.apply(style); } catch (e) { console.error(e); } };
    compute();
    const topics = ['settings', 'reload'].concat(o.topics || []);
    const link = IXC.connect({ role: 'overlay', topics, onState: (up) => { document.documentElement.classList.toggle('ixc-offline', !up); if (o.onState) o.onState(up); }, on: (m) => {
      if (m.type === 'settings.all') { saved = (m.values || {})['overlays.' + name] || {}; compute(); return; }
      if (m.type === 'settings') { if (m.key === 'overlays.' + name) { saved = m.value || {}; compute(); } return; }
      try { o.on && o.on(m, style); } catch (e) { console.error(e); } } });
    if (PREVIEW && o.demo) setTimeout(() => { try { o.demo(style); } catch (e) { console.error(e); } }, 300);
    return { link, get style() { return style; }, preview: PREVIEW };
  }
  // style values every overlay understands
  function applyBase(s) {
    const r = document.documentElement.style;
    if (s.font) r.setProperty('--font', `"${s.font.replace(/["<>;]/g, '')}", "Segoe UI", system-ui, sans-serif`);
    if (s.size) r.setProperty('--size', s.size + 'px'); if (s.color) r.setProperty('--color', safeColor(s.color, '#ffffff')); if (s.accent) r.setProperty('--accent', safeColor(s.accent, '#e3141e'));
    if (s.bg != null) r.setProperty('--bg', hexA(safeColor(s.bg, '#101014'), s.bgOpacity == null ? .75 : s.bgOpacity / 100));
    if (s.radius != null) r.setProperty('--radius', s.radius + 'px');
    const b = document.body.classList; [...b].filter(c => /^(align|layout|anim)-/.test(c)).forEach(c => b.remove(c));
    if (s.align) b.add('align-' + s.align); if (s.layout) b.add('layout-' + s.layout); if (s.animation) b.add('anim-' + s.animation);
    if (s.font && !/^(Segoe UI|Bahnschrift|Arial|Verdana|Tahoma|Georgia|Consolas|Impact|Trebuchet MS)$/i.test(s.font)) loadFont(s.font); }
  const loaded = new Set();
  function loadFont(f) { if (loaded.has(f) || !/^[\w \-]{2,40}$/.test(f)) return; loaded.add(f); const l = document.createElement('link'); l.rel = 'stylesheet'; l.href = 'https://fonts.googleapis.com/css2?family=' + encodeURIComponent(f) + ':wght@400;600;700;800&display=swap'; document.head.appendChild(l); }
  function safeColor(c, d) { return /^#[0-9a-f]{3,8}$/i.test(c || '') ? c : d; }
  function hexA(hex, a) { let h = hex.replace('#', ''); if (h.length === 3) h = h.split('').map(x => x + x).join(''); const n = parseInt(h.slice(0, 6), 16); return `rgba(${n >> 16 & 255},${n >> 8 & 255},${n & 255},${Math.max(0, Math.min(1, a))})`; }
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const time = (s) => { s = Math.max(0, Math.floor(+s || 0)); const h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), x = s % 60; return (h ? h + ':' + String(m).padStart(2, '0') : m) + ':' + String(x).padStart(2, '0'); };
  return { start, esc, time, q };
})();
