# Security policy

Only the latest release gets fixes. Please report vulnerabilities **privately** through
[GitHub private vulnerability reporting](https://github.com/infernoxc/ixc-chatbox/security/advisories/new), not in public issues.
You'll get a reply within 7 days.

## Security model (IXC 3)

**On your PC**
- IXC listens on **loopback only** (`127.0.0.1` and `::1`, port 8767 or the next free one). It uses its own small web server, not
  http.sys, so there are no URL reservations, firewall rules, router changes or admin rights.
- Every API call and WebSocket is refused when:
  - its `Origin` isn't IXC's own address (this includes the `"null"` origin of sandboxed frames and `file://` pages), or
  - its `Host` isn't `localhost`, `127.0.0.1` or `[::1]` (protects against DNS rebinding).
- Files are served only from IXC's own folders (no path escapes) and only with known file types. Local music files are served by a
  random id, never by path.
- **Secrets** (platform sign-ins, OBS / Streamer.bot passwords, API keys, Spotify keys, the phone relay key, phone device keys) are kept in
  `secrets.dat`, encrypted with Windows DPAPI for your Windows user. They are not in `config.json`, never sent to a web page or phone,
  and redacted in logs. Settings whose name contains key/secret/token/password are logged as "(hidden)".
- Upgrading from 2.x moves old passwords and keys out of `config.json` into `secrets.dat`.
- The installer needs **no admin rights**. It writes to `%LOCALAPPDATA%\IXC-OBS`, your Start menu, the per-user "start with Windows" entry
  and (with your choice) OBS's settings, after making a backup of each OBS file it changes.
- Updates are downloaded from this repository's GitHub releases and installed only if their **SHA-256** matches `SHA256SUMS.txt` of the
  same release. Settings are backed up first, and the previous version is put back if the new one doesn't start.
  The installer is **not code-signed** yet (see *Known limitations*).

**Diagnostics export**
- The zip contains logs, settings and the system check. Secrets are removed, tokens/keys in logs are redacted, and IP addresses
  (phones) and your Windows user name in folder paths are replaced by placeholders.
- It still contains your channel names and the names of viewers who used commands or triggered alerts (needed to understand a
  problem). With **detailed logs** on (Advanced), chat lines are logged too. You can open the zip and check it before sharing it.

**Phone remote (IXC relay)**
- IXC makes an **outgoing** WSS connection to the IXC relay (a Cloudflare Worker, `relay/`). Nothing on your PC is reachable from the
  internet, and the phone works on 4G/5G or any Wi-Fi.
- The PC owns a random relay address (`pcId`, 128 bits) and a secret (256 bits). The relay stores only `sha256(secret)`. Only the PC
  with the secret can take that address.
- The relay just forwards messages. It doesn't check phones and can't read or change settings: **your PC checks every phone.**
- **Pairing:**
  - the QR code holds a **one-time code** (144 bits) that works **once** and only for **5 minutes**. It is in the URL `#fragment`, so it
    isn't sent in HTTP request lines and doesn't appear in server logs. The QR code holds no permanent secret;
  - on pairing, the phone gets its own random **device key** (256 bits). The PC stores only its hash;
  - wrong codes or keys are limited to 6 per IP and 30 in total per 10 minutes.
- **Devices:** you can see, rename, disconnect and remove each phone, or remove all of them. A removed phone is disconnected at once.
  Phones not used for `remote.deviceDays` (90 by default) are forgotten. **Reset phone access** creates a new relay address, so every
  old QR code and phone stops working.
- After signing in, a phone can use only an **allow-list** of actions (chat, chat voice, music, read-only status and a short list of
  settings). It can't start pairing, manage phones, sign in to accounts, change other settings or reach PC-only APIs. More than 80
  messages in 10 seconds are refused.
- The relay's phone WebSocket refuses other websites (`Origin` must be the relay itself).
- Anyone holding a paired phone can **type in your chat** and control music. Remove phones you lose.

**Sign-in (Connect account)**
- Twitch uses the **device code** flow (public client, no secret).
- YouTube and Kick use the authorization code flow with **PKCE** and a `state` check. The redirect goes to IXC on `localhost`. The client
  secret stays in the relay, which only exchanges or refreshes the code (`/oauth/<p>/token`) and stores nothing.
- Tokens are refreshed automatically, stored in `secrets.dat`, and removed with **Sign out**.
- Reading chat needs **no sign-in**. Only replying, follower alerts and some viewer counts need one.

**Outbound connections**
- OBS and Streamer.bot on `127.0.0.1`;
- the platforms you turned on (Twitch, Kick, YouTube, Rumble) and their emote/avatar image servers;
- `speech.platform.bing.com` (neural voices; unofficial endpoint);
- YouTube / Spotify (song names, search, the player);
- the IXC relay (only when the phone remote is set up);
- `api.github.com` / `github.com` (update check and download).

No stream keys are stored, and no accounts are created.

## Known limitations
- The installer and `ixc-core.exe` are **not code-signed**, so Windows SmartScreen may warn on first download.
- DPAPI protects secrets from other Windows users and from copying the file to another PC. It doesn't protect them from programs running
  as your own Windows user.
- Kick chat reading, YouTube chat reading and the Edge neural voices use public but **undocumented** endpoints, which may change.
