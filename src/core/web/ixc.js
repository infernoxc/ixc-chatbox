// IXC page link - ONE WebSocket per page to IXC Core, with automatic reconnect. Updates are pushed, nothing polls.
// Part of IXC - Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
//   const link = IXC.connect({ role: 'player', topics: ['music.cmd'], on: (msg) => {...}, onState: (up) => {...} });
//   link.send({ type: 'music.cmd', c: {...} });   const r = await link.ask({ type: 'music.add', url }, 'music.added');
window.IXC = (() => {
  const defaultUrl = () => {
    if (/^https?:$/.test(location.protocol)) return (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws';
    return 'ws://localhost:' + (new URLSearchParams(location.search).get('helper') || 8767) + '/ws';
  };
  function connect(o) {
    let ws = null, up = false, retry = 400, stopped = false, rid = 0, lastMsg = Date.now(), timer = null;
    const out = [], waits = new Map(), url = o.url || defaultUrl();
    const set = (v) => { if (v !== up) { up = v; if (o.onState) try { o.onState(v); } catch (e) {} } };
    function open() {
      timer = null; if (ws || stopped) return;
      try { ws = new WebSocket(url); } catch (e) { return later(); }
      ws.onopen = () => {
        retry = 400; lastMsg = Date.now();
        if (o.session) { const s = o.session(); ws.send(JSON.stringify({ session: s || '' })); }
        ws.send(JSON.stringify({ type: 'hello', role: o.role || 'page', topics: o.topics || [] }));
        if (!o.session) { set(true); flush(); }
      };
      ws.onmessage = (e) => {
        lastMsg = Date.now(); let m; try { m = JSON.parse(e.data); } catch { return; }
        if (m.type === 'auth') { if (m.ok) { set(true); flush(); } else if (o.onAuthFailed) o.onAuthFailed(); return; }
        if (m.type === 'reload' && o.reload !== false) { try { o.beforeReload && o.beforeReload(); } catch (e) {} setTimeout(() => location.reload(), 400 + Math.random() * 600); return; }
        if (m.reqId && waits.has(m.reqId)) { const w = waits.get(m.reqId); waits.delete(m.reqId); clearTimeout(w.t); w.res(m); }
        if (o.on) try { o.on(m); } catch (err) { console.error(err); }
      };
      ws.onclose = () => { set(false); ws = null; if (!stopped) later(); };
      ws.onerror = () => { try { ws.close(); } catch (e) {} };
    }
    function later() { if (timer) return; timer = setTimeout(open, retry); retry = Math.min(retry * 2, 8000); }
    function flush() { while (out.length && ws && ws.readyState === 1) ws.send(out.shift()); }
    function send(obj) { const s = JSON.stringify(obj); if (up && ws && ws.readyState === 1) ws.send(s); else { out.push(s); if (out.length > 40) out.shift(); } }
    function ask(obj, _type, ms) { const id = 'r' + (++rid) + '_' + Date.now(); obj.reqId = id;
      return new Promise((res) => { waits.set(id, { res, t: setTimeout(() => { waits.delete(id); res({ ok: false, error: 'No answer - is IXC running?' }); }, ms || 20000) }); send(obj); }); }
    // keep-alive: detects dead connections (sleeping phones, network changes) and reconnects
    setInterval(() => { if (!ws) return; if (up) send({ type: 'ping', t: Date.now() }); if (Date.now() - lastMsg > 70000) { try { ws.close(); } catch (e) {} } }, 25000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden && !ws) { clearTimeout(timer); timer = null; retry = 400; open(); } });
    open();
    return { send, ask, get up() { return up; }, close() { stopped = true; try { ws && ws.close(); } catch (e) {} } };
  }
  return { connect };
})();
