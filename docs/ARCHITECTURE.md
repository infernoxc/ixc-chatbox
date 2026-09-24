# Architecture

```
OBS Studio                                     your PC                                         internet
┌──────────────────────────────┐   WS    ┌──────────────────────────────┐
│ Dock "IXC ChatBox"            │ ──────▶ │ Streamer.bot WebSocket :8080  │ ──▶ Twitch / Kick / YouTube
│   /chat/chat.html?dock=1      │         └──────────────▲───────────────┘
│ Overlay /chat/chat.html       │   HTTP  ┌──────────────┴───────────────┐
│   (reply box, ! and @ lists) ─┼───────▶ │ Chat relay  :8768 (localhost) │   authenticated Streamer.bot client
│                               │         │ src/chat/relay/ChatRelay.cs   │   (password read on this PC)
│ Browser source                │   HTTP  ├──────────────────────────────┤
│   "IXC ChatBox TTS" /chat/tts ┼───────▶ │ IXC Helper  :8767 (localhost) │ ──▶ Microsoft neural voices (TTS)
└──────────────────────────────┘         │ src/helper/ixc-helper.ps1     │
  phone (optional, private Wi-Fi, key) ─▶ relay :8768 (only after enable-phone-access.ps1)
```

| File | Role |
|---|---|
| `src/chat/chat.html` | The chat UI: overlay, dock and phone modes, emotes, avatars, badges, viewer counts, TTS controls, reply box and suggestions. Reads chat straight from Streamer.bot; the phone view polls the relay. |
| `src/chat/tts.html` | Plays queued TTS MP3s inside OBS (1 fps browser source). |
| `src/chat/relay/ChatRelay.cs` | C# 5, compiled at start by the Windows-built-in compiler (`Add-Type`). One authenticated Streamer.bot connection (`SendMessage`, `GetCommands`, `GetActiveViewers`), the last 300 chat events, and an HTTP API. |
| `src/chat/relay/chat-relay.ps1` | Loads config, finds Streamer.bot's settings, creates the phone key, starts the relay. |
| `src/helper/ixc-helper.ps1` | Serves the pages and the TTS queue. Shared with [IXC Music](https://github.com/infernoxc/ixc-music) (which is why it also has music endpoints). |
| `src/helper/edge-tts.ps1` | Minimal TLS WebSocket client for Microsoft Edge's "Read aloud" voices. |

## Relay API (`http://localhost:8768`)
| Path | Notes |
|---|---|
| `GET /api/ping` | `{ok, app, streamerbot, auth, phone, log}` |
| `GET /api/events?since=N` | Raw Streamer.bot events (`since=-1` returns the last 60) |
| `POST /api/send` | `{"platform":"all","message":"..."}` (platform: `all`, `twitch`, `kick` or `youtube`) returns per-platform results |
| `GET /api/suggest` | `{"commands":["!..."],"users":[{name,platform}]}` |
| `GET /api/phone` | PC only: the phone URL(s) with key, or `{"disabled":true}` |

Requests from other devices need `?key=<phone key>`, and they're only possible when phone access is on.

## Helper TTS API (`http://localhost:8767`)
`POST /api/say {"text","voice"}` → `{n}` · `GET /api/says?since=N` → `{seq, skip, err, items}` · `GET /api/tts.mp3?n=N` → `audio/mpeg` · `POST /api/skip`.
`/api/*` rejects requests with a foreign `Origin` header.

## Data
`%LOCALAPPDATA%\IXC-OBS\`: `app\`, `config.json`, `phone_key.txt`, `channel_names.txt`, `helper.log`, `chat-relay.log`.
TTS preferences are stored in OBS's browser storage.