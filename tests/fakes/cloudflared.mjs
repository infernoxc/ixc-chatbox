#!/usr/bin/env node
// Stand-in for Cloudflare's cloudflared in tests: prints what a quick tunnel prints and stays running until it is killed.
// (IXC is reached directly in tests; the tunnel address is mapped to 127.0.0.1 by the test.)
const host = process.env.IXC_FAKE_TUNNEL_HOST || 'ixc-test-tunnel.trycloudflare.com';
if (process.env.IXC_FAKE_TUNNEL_FAIL) { console.error('2026-10-02T00:00:00Z ERR failed to request quick Tunnel: 429 Too Many Requests'); process.exit(1); }
console.error('2026-10-02T00:00:00Z INF Requesting new quick Tunnel on trycloudflare.com...');
console.error('2026-10-02T00:00:00Z INF |  https://' + host + '                                    |');
setTimeout(() => console.error('2026-10-02T00:00:01Z INF Registered tunnel connection connIndex=0 event=0 protocol=http2'), 300);
setInterval(() => {}, 1000);
