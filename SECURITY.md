# Security policy

Only the latest release gets fixes. Please report vulnerabilities **privately** through
[GitHub private vulnerability reporting](https://github.com/infernoxc/ixc-chatbox/security/advisories/new), not in public issues.
You'll get a reply within 7 days.

## Security model
**On your PC**
- IXC Core listens on **localhost only** (`127.0.0.1:8767`). Nothing listens on your network, and no firewall rules or router changes are made.
- The API and WebSocket refuse requests from other websites (foreign `Origin`). Files are served only from IXC's own folders (no path escapes), and only known file types.
- **Streamer.bot and OBS passwords** are read on your PC from `config.json` or the programs' own settings files, used only for `127.0.0.1`
  connections, and never sent to a web page, phone or server.
- The installer needs **no admin rights**. It builds IXC Core from its source with the C# compiler built into Windows, and writes to:
  - `%LOCALAPPDATA%\IXC-OBS`;
  - your Start menu;
  - one per-user scheduled task ("IXC for OBS");
  - with `-AddObsDocks` only: the dock line of OBS's `user.ini`, after making a backup.

**Phone remote (off until you open the 📱 QR code)**
- A second listener on **`127.0.0.1:8769`** is reached only through Cloudflare's `cloudflared` tunnel. The tunnel makes an outbound connection, so no ports are opened, and the phone always uses **HTTPS/WSS**.
- `cloudflared.exe` is downloaded once from Cloudflare's official GitHub release. It's kept only if Windows confirms a **valid Authenticode signature from Cloudflare, Inc.**
- Through the tunnel, only four things exist: the phone page, `POST /api/pair`, a health check and one WebSocket. Every other path returns 404. The `Host` header must be the current tunnel address, and `Origin` must match it.
- **Pairing:**
  - the QR code holds a random 144-bit one-time code, valid for 5 minutes, in the URL `#fragment`, so it never appears in HTTP request lines or logs;
  - the phone trades it for a random 256-bit session token, which is kept in memory only (stored as a hash);
  - sessions expire after `remote.sessionHours` without use (at most 7 days), and restarting IXC ends all sessions;
  - wrong codes are limited to 5 per network per 10 minutes (and 30 in total).
- A phone must authenticate on the WebSocket within 5 seconds. It can then use only an allow-list of actions: chat, TTS, music controls and read-only status. It can't change config, start pairing or reach PC-only APIs. More than 60 messages in 10 seconds disconnects it.
- The tunnel stops after `remote.idleMinutes` (30) without a phone, and when IXC stops. Windows kills it together with IXC even if IXC crashes (job object).
- Anyone holding a signed-in phone can **type in your chat**. Use **Turn phone access off** in the QR window to sign every phone out.

**Outbound connections**
- Streamer.bot and OBS on `127.0.0.1`;
- `speech.platform.bing.com` (neural voices);
- emote and avatar CDNs;
- `cdnjs.cloudflare.com` (QR library, loaded only in the QR window);
- only when you use the phone remote: `github.com` (the one-time cloudflared download) and Cloudflare's tunnel network.

No stream keys, OAuth tokens or account passwords are stored, and no accounts are created.
