# Configuration

Most settings change live from the dock (TTS) or phone. Everything is stored in **`%LOCALAPPDATA%\IXC-OBS\config.json`**
(Start menu › IXC for OBS › IXC settings). If you edit the file by hand: **Stop IXC**, edit, **Start IXC**. Updates keep your file.

## General
| Setting | Default | Meaning |
|---|---|---|
| `helper.port` | `8767` | Port of IXC Core (pages + API). If you change it, change the OBS URLs too. |
| `diagnostics.enabled` | `true` | `false` turns off the diagnostics page and `/api/diag`. |
| `diagnostics.verboseLog` | `false` | Also write routine events to `ixc-core.log` (errors are always logged). |

## Chat TTS (`tts`)
| Setting | Default | Meaning |
|---|---|---|
| `on` | `false` | TTS on/off (the dock button). |
| `voice` | `in-male` | `in-male`, `in-female`, `in-male-2`, `in-female-2`, `us-male`, `us-female`, `jarvis`, `local` (offline Windows voice). |
| `readMode` | `name` | `all` (just the message), `name` ("name says message") or `tts` (only `!tts` messages). |
| `speed` / `pitch` / `volume` | `0` / `0` / `100` | Percent (-50…100) / Hz (-30…30) / percent, on top of each voice's own tuning. |
| `maxChars` | `200` | Longer messages are cut at a word. |
| `queueMax` | `6` | Waiting messages; when full, the oldest normal one is dropped. |
| `staleSec` | `60` | A message still waiting after this many seconds is skipped. |
| `perMinute` | `20` | Messages read per minute at most (bursts beyond it are skipped). |
| `userCooldownSec` | `3` | Minimum gap between two messages of the same person. |
| `ignoreOwn` / `ignoreBots` | `true` / `true` | Skip your own accounts (found from Streamer.bot, plus `ownNames`) / known bots (plus `botNames`). |
| `readEmotes` / `readEmoji` / `readLinks` | `false` | Read emote names / emoji / full links. |
| `fallback` | `true` | Use the offline Windows voice when the neural voice fails. |
| `ownNames`, `botNames`, `neverSpeak`, `blockedWords` | `[]` | Lists of names / words. |
| `voices` | `{}` | Override or add voices: `"key": { "voice": "<Microsoft neural voice>", "rate": "+5%", "pitch": "-2Hz", "label": "..." }`. |

## Chat (`chat`, `streamerbot`)
| Setting | Default | Meaning |
|---|---|---|
| `chat.hiddenCommands` | `[]` | Commands left out of `!` suggestions, e.g. `["!mod"]`. |
| `chat.extraCommandsFile` | `""` | Optional text or code file; every `"name",` or `case "name":` in it is suggested as `!name`. |
| `streamerbot.websocketUrl` | `ws://127.0.0.1:8080/` | Streamer.bot WebSocket server. |
| `streamerbot.password` | `""` | Optional; if empty, it's read from Streamer.bot's own settings. |
| `streamerbot.settingsPath` | `auto` | Path to Streamer.bot's `data\settings.json`. `auto` finds the running Streamer.bot, or `Streamer.bot*` folders in Desktop, Documents, Downloads, your user folder, `C:\` and `D:\`. |

## Phone remote (`remote`)
| Setting | Default | Meaning |
|---|---|---|
| `enabled` | `true` | `false` removes the phone feature completely. |
| `port` | `8769` | Local port the tunnel connects to (listens on `127.0.0.1` only). |
| `sessionHours` | `12` | A phone is signed out after this many hours without use (at most 7 days in total). |
| `idleMinutes` | `30` | The tunnel closes after this many minutes without a phone. |
| `allowDownload` | `true` | Allow the one-time download of Cloudflare's `cloudflared.exe` (signature-checked). |
| `cloudflaredPath` | `""` | Use your own `cloudflared.exe` instead. |

## URL options
Add them to `chat.html?...`, separated by `&`.

| Option | Default | Meaning |
|---|---|---|
| `dock=1` | off | Dock mode. Without it you get the overlay. |
| `viewers=1` | off | Viewer-count bar. |
| `size` | 14 dock / 16 overlay | Text size in px. |
| `max` | 150 dock / 14 overlay | Messages kept. |
| `fade` | 0 dock / 90 overlay | Seconds until a message fades (0 = never). |
| `alpha` | 1 dock / .55 overlay | Bubble background opacity. |
| `avatars=0`, `badges=0` | on | Hide avatars / badges. |
| `direct=1` (+ `host`, `port`) | off | Overlay reads Streamer.bot directly, without IXC (no replies or TTS). |
| `demo=1` | off | Demo messages. |
