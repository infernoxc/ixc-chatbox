// IXC page link - ONE WebSocket per page to IXC, with automatic reconnect (exponential back-off) and keep-alive.
// Updates are pushed, nothing polls. Part of IXC - Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
//   const link = IXC.connect({ role: 'player', topics: ['music.player'], on: (msg) => {...}, onState: (up) => {...} });
//   link.send({ type: 'music.cmd', c: {...} });   const r = await link.ask({ type: 'music.add', url }, 'music.added');
//   const d = await IXC.api('/api/settings', { patch: {...} });   // POST when a body is given
window.IXC = (() => {
  const wsUrl = () => (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws';
  function connect(o) {
    let ws = null, up = false, attempt = 0, stopped = false, rid = 0, lastMsg = Date.now(), timer = null;
    const out = [], waits = new Map(), url = o.url || wsUrl();
    const set = (v) => { if (v !== up) { up = v; if (o.onState) try { o.onState(v); } catch (e) { console.error(e); } } };
    function open() {
      timer = null; if (ws || stopped) return;
      try { ws = new WebSocket(url); } catch (e) { return later(); }
      ws.onopen = () => {
        attempt = 0; lastMsg = Date.now();
        if (o.beforeHello) try { o.beforeHello(ws); } catch (e) {}
        ws.send(JSON.stringify({ type: 'hello', role: o.role || 'page', topics: o.topics || [] }));
        if (!o.auth) { set(true); flush(); }
      };
      ws.onmessage = (e) => {
        lastMsg = Date.now(); let m; try { m = JSON.parse(e.data); } catch { return; }
        if (o.auth && (m.type === 'auth' || m.type === 'paired')) { if (m.ok) { set(true); flush(); } }
        if (m.type === 'reload' && o.reload !== false) { try { o.beforeReload && o.beforeReload(); } catch (e) {} setTimeout(() => location.reload(), 400 + Math.random() * 800); return; }
        if (m.reqId && waits.has(m.reqId)) { const w = waits.get(m.reqId); waits.delete(m.reqId); clearTimeout(w.t); w.res(m); }
        if (o.on) try { o.on(m); } catch (err) { console.error(err); }
      };
      ws.onclose = () => { set(false); ws = null; if (!stopped) later(); };
      ws.onerror = () => { try { ws.close(); } catch (e) {} };
    }
    function later() { if (timer) return; const d = Math.min(15000, 400 * Math.pow(2, attempt++)) * (0.8 + Math.random() * 0.4); timer = setTimeout(open, d); }
    function flush() { while (out.length && ws && ws.readyState === 1) ws.send(out.shift()); }
    function send(obj) { const s = JSON.stringify(obj); if (up && ws && ws.readyState === 1) ws.send(s); else { out.push(s); if (out.length > 60) out.shift(); } }
    function ask(obj, _type, ms) { const id = 'r' + (++rid) + '_' + Date.now(); obj.reqId = id;
      return new Promise((res) => { waits.set(id, { res, t: setTimeout(() => { waits.delete(id); res({ ok: false, error: 'No answer - is IXC running?' }); }, ms || 20000) }); send(obj); }); }
    // keep-alive: detects dead connections (sleeping PCs/phones, network changes) and reconnects
    setInterval(() => { if (!ws) return; if (up) send({ type: 'ping', t: Date.now() }); if (Date.now() - lastMsg > 70000) { try { ws.close(); } catch (e) {} } }, 25000);
    const now = () => { if (!ws && !stopped) { clearTimeout(timer); timer = null; attempt = 0; open(); } };
    document.addEventListener('visibilitychange', () => { if (!document.hidden) now(); });
    window.addEventListener('online', now);
    open();
    return { send, ask, get up() { return up; }, close() { stopped = true; try { ws && ws.close(); } catch (e) {} } };
  }
  async function api(path, body) {
    try {
      const r = await fetch(path, body === undefined ? { cache: 'no-store' } : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      const t = await r.text(); let d; try { d = JSON.parse(t); } catch { d = { ok: false, error: t || ('HTTP ' + r.status) }; }
      if (!r.ok && d && d.ok === undefined) d.ok = false; return d;
    } catch (e) { return { ok: false, error: 'IXC is not running' }; }
  }
  return { connect, api };
})();
