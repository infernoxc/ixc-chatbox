# Architecture

```
OBS Studio                                   your PC                                            internet
┌─────────────────────────────┐  one WS   ┌───────────────────────────────────────┐  WS   ┌─────────────────────┐
│ Dock   /chat/chat.html?dock │ ◀───────▶ │ IXC Core  localhost:8767              │ ◀───▶ │ Streamer.bot :8080  │ ─▶ Twitch / Kick / YouTube
│ Overlay /chat/chat.html     │ ◀──────── │  src/core/*.cs (ixc-core.exe)         │       └─────────────────────┘
│ Source /chat/tts.html       │ ◀──────── │  chat · TTS queue · music · diagnostics│ ───▶ Microsoft neural voices (TTS)
└─────────────────────────────┘  (push)   │  remote listener 127.0.0.1:8769 ─────┐│
                                          └───────────────────────────────────────┘│
phone (mobile data) ══ HTTPS/WSS ══▶ Cloudflare ══▶ cloudflared.exe (outbound tunnel) ─┘
```

**IXC Core** is one small native program (C# 5 on .NET Framework 4.8), built on your PC by `src/core/build-core.ps1` with the compiler that ships with Windows.
It replaces the v1 PowerShell helper and chat relay. There's one Streamer.bot connection for everything, and pages get updates **pushed**
over one WebSocket each, so nothing polls.

| File | Role |
|---|---|
| `src/core/Core.cs` | HTTP server, WebSocket hub (topics), config, diagnostics, file serving (no path escapes) |
| `src/core/Links.cs` | Reconnecting clients for Streamer.bot (authenticated) and OBS (obs-websocket 5) |
| `src/core/Chat.cs` | Chat events → dedup (by message id) → pages, phones and TTS; replies, `!`/`@` suggestions |
| `src/core/Tts.cs` | TTS filters, text cleanup, queue policy, Edge neural voices, offline Windows (SAPI) fallback |
| `src/core/Music.cs` | IXC Music: dock ↔ player, YouTube/Spotify links, audio destination (only when IXC Music is installed) |
| `src/core/Remote.cs` | Phone access: cloudflared tunnel, one-time pairing, sessions, rate limits |
| `src/core/web/` | `ixc.js` (page ↔ core link), `phone.js` (QR dialog), `mobile.html` (phone UI), `diag.html` |
| `src/chat/chat.html`, `tts.html` | Chat UI (dock + overlay) and the TTS player source |

## TTS pipeline
`Streamer.bot event → duplicate id? → TTS on? → mirrored on another platform (same person + text within 15 s)? → own account / bot /
never-speak / blocked word? → command or !tts? → per-user and per-minute limits → cleanup → queue (max size; drop oldest normal item)`
→ when it's its turn: skip if older than `staleSec` → synthesize (next item is prepared while the current one plays) →
`tts.play` to the OBS source → the source reports `tts.done` → next. A watchdog moves on if a source never reports back.
Only the last 5 items keep their audio in memory (for replay).

## HTTP API (`http://localhost:8767`, requests from other websites are refused)
| Path | Notes |
|---|---|
| `GET /api/ping` · `GET /api/diag` | status · full diagnostics |
| `POST /api/chat/send` `{platform, message}` · `GET /api/chat/suggest` · `GET /api/chat/history` | chat |
| `GET/POST /api/tts/settings` · `GET /api/tts/state` · `POST /api/tts/say {text, voice}` · `POST /api/tts/skip` · `/clear` · `/replay {n}` · `GET /api/tts/audio?n=` | TTS |
| `GET /api/remote/status` · `POST /api/remote/pair` · `/stop` · `/revoke` | phone access (PC only) |
| v1: `POST /api/say`, `POST /api/skip`, `GET /api/tts.mp3?n=` | still work |

## WebSocket (`/ws`)
Send `{"type":"hello","role":"chat","topics":["chat","tts"]}` first. Topics: `chat`, `tts`, `tts.play`, `music.state`, `music.cmd`, `remote`, `diag`, `reload`.
Messages: `chat.send`, `chat.suggest`, `tts.set {patch}`, `tts.test`, `tts.say`, `tts.skip`, `tts.clear`, `tts.replay`, `diag.get`, `ping`.
Phones use the same protocol through the tunnel, after `{"session": "<token>"}`, and only with an allow-list of topics and messages.

## Data
`%LOCALAPPDATA%\IXC-OBS\`: `app\` (program), `config.json`, `ixc-core.log`, `bin\cloudflared.exe` (only after you first use the phone remote).
