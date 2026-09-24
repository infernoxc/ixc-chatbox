# Security policy

Only the latest release gets fixes. Please report vulnerabilities **privately** through
[GitHub private vulnerability reporting](https://github.com/infernoxc/ixc-chatbox/security/advisories/new), not in public issues.
You'll get a reply within 7 days.

## Security model
- The helper (`:8767`) and the chat relay (`:8768`) listen on **localhost only** by default.
- `/api/*` on the helper rejects requests from other websites (foreign `Origin`). Files are served only from the app folders, with path traversal blocked.
- **Streamer.bot password:** read on your PC from `config.json` or Streamer.bot's own `data\settings.json`. It's used only by the relay
  to talk to Streamer.bot on `127.0.0.1`, and never sent to a web page, phone or remote server.
- **Phone view:** off by default. `enable-phone-access.ps1` (run as admin) adds an HTTP URL reservation and a firewall rule for
  **private** networks only. Every request from another device must carry the random key in `phone_key.txt`, and anyone with
  the phone link can post in your chat, so keep it private. Delete `phone_key.txt` to rotate the key; `-Disable` removes the rule.
- No stream keys, OAuth tokens or account passwords are stored; no accounts are created. Messages are sent through your own Streamer.bot.
- The installer needs **no admin rights**. It writes to `%LOCALAPPDATA%\IXC-OBS`, your Start menu, one per-user scheduled task
  ("IXC for OBS"), and (only with `-AddObsDocks`) the `ExtraBrowserDocks` line of OBS's `user.ini`, after a backup.
- Outbound connections: Streamer.bot on `127.0.0.1`, `speech.platform.bing.com` (TTS), emote/avatar CDNs, and `cdnjs.cloudflare.com` (QR library, only in the phone dialog).