// IXC phone pairing popup for the PC docks: starts the secure tunnel and shows a one-time QR code.
// Part of IXC - Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
window.IXCPhone = (() => {
  const api = (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8767') + '/api/remote/';
  let box, timer;
  function el() {
    if (box) return box;
    const css = document.createElement('style');
    css.textContent = `#ixcPhone{position:fixed;inset:0;z-index:99;background:rgba(0,0,0,.86);display:none;align-items:center;justify-content:center;font:13px Bahnschrift,"Segoe UI",sans-serif;color:#ddd;padding:12px}
      #ixcPhone.show{display:flex}#ixcPhone .c{background:#16151b;border:1px solid #34323c;border-radius:12px;padding:14px;max-width:330px;width:100%;text-align:center;line-height:1.4}
      #ixcPhone h3{margin:0 0 6px;color:#fff;font-size:15px}#ixcPhone .qr{background:#fff;padding:10px;border-radius:8px;display:inline-block;margin:8px 0;min-height:40px}
      #ixcPhone .s{color:#9b98a4;font-size:12px}#ixcPhone .ok{color:#6be38a}#ixcPhone .bad{color:#ffcf6b}#ixcPhone button{margin:8px 3px 0;background:#1f1e25;border:1px solid #34323c;color:#eee;border-radius:7px;padding:6px 12px;font:600 12px Bahnschrift,sans-serif;cursor:pointer}
      #ixcPhone button.red{background:#e3141e;border-color:#e3141e;color:#fff}#ixcPhone code{display:block;word-break:break-all;font-size:11px;color:#bbb;margin-top:4px}`;
    document.head.appendChild(css);
    box = document.createElement('div'); box.id = 'ixcPhone';
    box.innerHTML = `<div class="c"><h3>Control IXC from your phone</h3><div class="st s">Starting the secure connection…</div><div class="qr" style="display:none"></div><code></code>
      <div class="s" style="margin-top:6px">Works on mobile data or any Wi-Fi. The code works <b>once</b> and expires in <b class="left">5:00</b>.<br>Only scan it yourself - whoever scans it can type in your chat.</div>
      <button class="new">New code</button><button class="stop">Turn phone access off</button><button class="red close">Close</button></div>`;
    document.body.appendChild(box);
    box.querySelector('.close').onclick = () => { box.classList.remove('show'); clearInterval(timer); };
    box.querySelector('.new').onclick = () => open();
    box.querySelector('.stop').onclick = async () => { await fetch(api + 'revoke', { method: 'POST' }).catch(() => {}); await fetch(api + 'stop', { method: 'POST' }).catch(() => {}); status('Phone access is off and all phones were signed out.', 'ok'); qr(''); };
    return box;
  }
  const status = (t, cls) => { const s = box.querySelector('.st'); s.textContent = t; s.className = 'st ' + (cls || 's'); };
  async function qr(url) {
    const q = box.querySelector('.qr'); q.innerHTML = ''; box.querySelector('code').textContent = url ? url.replace(/#.*/, '#…') : ''; q.style.display = url ? 'inline-block' : 'none'; if (!url) return;
    if (!window.QRCode) await new Promise((ok) => { const s = document.createElement('script'); s.src = 'https://cdnjs.cloudflare.com/ajax/libs/qrcodejs/1.0.0/qrcode.min.js'; s.onload = ok; s.onerror = ok; document.head.appendChild(s); });
    if (window.QRCode) new QRCode(q, { text: url, width: 220, height: 220, correctLevel: QRCode.CorrectLevel.M }); else q.textContent = '(the QR picture needs internet)';
  }
  async function open() {
    el().classList.add('show'); clearInterval(timer); qr(''); status('Starting the secure connection (first time: downloading Cloudflare\'s tunnel program, ~60 MB)…');
    let d; try { d = await (await fetch(api + 'pair', { method: 'POST' })).json(); } catch (e) { d = { ok: false, error: 'IXC is not running.' }; }
    if (!d.ok) { status('✗ ' + (d.error || 'Could not start phone access.'), 'bad'); return; }
    status(d.reachable ? '✓ Online - scan with your phone camera:' : 'Online - scan with your phone camera (still confirming it is reachable from the internet):', 'ok'); qr(d.pairUrl);
    let left = d.expiresIn || 300; const L = box.querySelector('.left');
    timer = setInterval(() => { left--; L.textContent = Math.floor(left / 60) + ':' + String(left % 60).padStart(2, '0'); if (left <= 0) { clearInterval(timer); qr(''); status('Code expired - press New code.', 'bad'); } }, 1000);
  }
  return { open };
})();
