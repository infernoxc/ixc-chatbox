# Architecture

```
 OBS Studio                                   this PC                                                    internet
┌──────────────────────────────┐       ┌───────────────────────────────────────────────┐
│ Docks: /chat /music /app     │◀─WS──▶│ IXC (ixc-core.exe)          127.0.0.1:8767   │──▶ Twitch IRC + EventSub, Helix
│ Scene "IXC Audio":           │       │  Web.cs     HTTP + WebSocket (loopback only)  │──▶ Kick chat socket + API
│   /music/player.html (audio) │◀─WS──▶│  Chat.cs    one pipeline for all platforms    │──▶ YouTube live chat + Data API
│   /chat/tts.html     (audio) │       │  Tts.cs     queue, voices, ducking            │──▶ Rumble Live Stream API
│ Overlays: /overlay/*.html    │◀─WS───│  Music.cs   queue (music.json), YouTube, local│──▶ Microsoft neural voices
└──────────────────────────────┘       │  Obs.cs     obs-websocket 5 (setup, routing)  │──▶ GitHub releases (updates)
 Dashboard window: /app/ ◀─WS─────────▶│  Remote.cs  phone relay link (outgoing)       │──┐
                                       │  Supervisor.cs  tray + crash restart          │  │  WSS (out)
                                       └───────────────────────────────────────────────┘  ▼
 phone (4G/5G/Wi-Fi) ══ HTTPS/WSS ══▶ IXC relay (Cloudflare Worker + Durable Object, relay/) ◀══┘
```

- **One program.** `ixc-core.exe` without arguments is the tray **supervisor**. It starts `ixc-core.exe --worker` and restarts it after a crash: at most 3 times in 10 minutes, then it asks the user. The exit codes are 3 (restart me) and 4 (quit).
- **Own web server** (`Web.cs`): HTTP/1.1 + WebSocket on `127.0.0.1` and `::1` only. It doesn't use http.sys, so there are no URL reservations or admin rights. If the port is taken by another program, IXC moves to the next free one and updates its OBS sources.
- **Pushed updates.** Every page keeps one WebSocket (`src/core/web/ixc.js`), subscribes to topics, and reconnects with back-off. Messages to each page are sent strictly in order. A page that stops reading is dropped instead of using up memory.
- **State lives in IXC, not in pages:**
  - settings in `config.json` (validated by `Settings.cs`, written atomically with rolling backups),
  - tokens and keys in `secrets.dat` (encrypted with DPAPI for the Windows user),
  - the music queue in `music.json`.

  Pages can reload at any time without losing anything.
- **Isolation.** Each chat platform is its own `ChatSource`, with its own thread or link, state and back-off. `NetWatch` wakes every connection at once when the network changes or the PC wakes from sleep.

## Files
| File | Role |
|---|---|
| `src/core/Program.cs` | Entry point, routing, shared API, port selection, single instance per data folder |
| `src/core/Web.cs` | HTTP/WebSocket server |
| `src/core/Core.cs` | JSON, settings file, secrets (DPAPI), logs (one file per area, secrets redacted), WebSocket hub |
| `src/core/Settings.cs` | Every setting: type, default, limits; validation; upgrades from v2 |
| `src/core/Links.cs` | Reconnecting WebSocket client base (OBS, Twitch, Kick, Streamer.bot, relay) + network/sleep watcher |
| `src/core/Platforms.cs` | Twitch, Kick, YouTube, Rumble, Streamer.bot sources; viewer polling |
| `src/core/Accounts.cs` | Sign-in (Twitch device flow; YouTube/Kick with PKCE through the relay), token refresh, platform APIs |
| `src/core/Chat.cs` | Normalized message, de-duplication, filters, history, moderation, sending |
| `src/core/Commands.cs`, `SongRequests.cs` | Chat commands with permissions/cooldowns; song requests with limits |
| `src/core/Tts.cs` | TTS decisions, cleanup, per-person/role/platform voices, queue, playback, ducking triggers; Edge + SAPI engines |
| `src/core/Music.cs`, `Local.cs` | Queue and playback control, YouTube/Spotify resolving, local files (by id, with Range), OBS audio routing |
| `src/core/Obs.cs` | OBS detection, WebSocket enable, panels (docks), auto setup, overlays, cleanup on uninstall |
| `src/core/Viewers.cs` | Viewer counts with freshness, Now Playing, text outputs, status summary |
| `src/core/Remote.cs` | Relay link, pairing, device keys, phone permissions |
| `src/core/Health.cs` | System check, repairs, backups, diagnostics export, updates |
| `src/core/Supervisor.cs` | Tray icon + crash supervisor (Windows) |
| `src/app/` | Dashboard + first-run wizard |
| `src/chat/`, `src/music/` | OBS docks and the two audio sources |
| `src/overlay/` | On-stream overlays (`overlay.js` = shared runtime) |
| `relay/` | Cloudflare Worker: phone page, PC↔phone forwarding, OAuth helper |

## Music player protocol
- **IXC → player** (topic `music.player`):
  - `music.play {item, startAt, autoplay, volume, duck}`
  - `music.ctl {op: pause|resume|stop|seek|volume|duck, level, fadeMs}`
- **Player → IXC:** `music.ev {ev: ready|loaded|playing|paused|ended|error|progress, uid, pos, dur, title, code}`

When the player says `ready`, IXC resumes the current song at the last position. Ducking is a multiplier on top of the normal volume, so the normal volume is never changed.

## Phone relay protocol
- **PC → relay** `wss://relay/pc/<pcId>`:
  - The PC sends `{t:"hello", secret}`. The relay stores only `sha256(secret)`; the first PC to use a (random) id owns it.
  - Frames: `{t:"open"|"msg"|"close", c:<connection>, d:<text>}`.
- **Phone → relay** `wss://relay/phone/<pcId>`. The relay tells the phone `{relay:"pc-online"|"pc-offline"}`.
- **Phone → PC:**
  - First message: `{type:"pair", code}` (one-time code, 5 minutes) or `{type:"auth", deviceId, token}`.
  - After that, the normal page protocol, limited to an allow-list of topics, messages and settings.

## HTTP API (local; IXC's own pages, or tools without a browser Origin)
| Path | |
|---|---|
| `GET /api/ping` · `/api/status` · `/api/diag` · `/api/logs?area=` | status |
| `GET/POST /api/settings` `{patch:{key:value}}` | all settings (validated) |
| `GET /api/health` · `POST /api/repair {action}` · `GET/POST /api/backups` · `POST /api/diagnostics/export` | system |
| `POST /api/chat/send {platform, message}` · `GET /api/chat/history` · `/api/chat/status` · `/api/chat/suggest` | chat |
| `GET /api/tts/state` · `POST /api/tts/say|skip|clear|replay|user` | chat voice |
| `GET /api/music/state` · `POST /api/music/cmd {cmd,...}` · `POST /api/music/add {url, mode}` · `GET /api/music/search?q=` · `/api/music/route` | music |
| `GET /api/nowplaying(.txt)` · `/api/viewers(.txt?p=total|twitch|...)` | outputs for OBS / Streamer.bot / other tools |
| `GET /api/accounts` · `POST /api/accounts/<p>/connect|signout|cancel` | sign-in |
| `GET /api/remote/status` · `POST /api/remote/pair|cancel|rename|revoke|disconnect` | phones |
| `GET /api/obs/detect|check` · `POST /api/obs/overlay|remove` | OBS |
| `GET /api/update/check` · `POST /api/system/restart|quit` | updates, process |

Requests whose `Origin` is not IXC's own address are refused, including the `"null"` origin of sandboxed frames and `file://`.
Requests whose `Host` is not `localhost`, `127.0.0.1` or `[::1]` are refused too (DNS rebinding).

## Tests
- `tests/integration/*.test.mjs` (Node) build IXC with Mono and run it against fake servers:
  - Twitch IRC/EventSub/Helix, Kick API + chat socket, YouTube, Rumble, OBS, Streamer.bot, GitHub,
  - the real relay code in Cloudflare's local runtime.
- `tests/ui/ui.test.mjs` drives the dashboard, docks, overlays and phone page in Chromium.
- `tests/smoke.ps1` runs on Windows: it builds with Windows' own compiler, then tests installing, updating, crash recovery and uninstalling, with both the script and the real `IXC-Setup.exe`.
- `tests/live.ps1` checks the real platforms. CI reports it but doesn't fail on it, because results depend on who's live.
