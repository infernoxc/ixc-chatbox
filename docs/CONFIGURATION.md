# Configuration

## config.json
**`%LOCALAPPDATA%\IXC-OBS\config.json`** (Start menu › IXC for OBS › IXC settings). Updates keep it. After editing it:
**Stop IXC**, then **Start IXC**.

| Setting | Default | Meaning |
|---|---|---|
| `helper.port` | `8767` | Port of the local IXC Helper (serves the pages and TTS). If you change it, change the OBS URLs and add `&helper=<port>`. |
| `tts.enabled` | `true` | `false` turns off the neural voices (the dock then uses the local Windows voice). |
| `tts.maxChars` | `240` | Longest message that is read. |
| `tts.voices` | 5 voices | `name → { "voice": "<Microsoft neural voice>", "rate": "+0%", "pitch": "+0Hz" }`. The dock offers the keys `in-male`, `in-female`, `us-male`, `us-female` and `jarvis`, so change their `voice` to use other voices ([voice list](https://learn.microsoft.com/azure/ai-services/speech-service/language-support?tabs=tts)). |
| `chatRelay.enabled` | `true` | `false` turns off replying, suggestions and the phone view. |
| `chatRelay.port` | `8768` | Relay port. If you change it, add `&relay=<port>` to the dock URL. |
| `chatRelay.phoneAccess` | `false` | Set by `enable-phone-access.ps1`. Don't edit it by hand. |
| `chatRelay.hiddenCommands` | `[]` | Commands to leave out of `!` suggestions, e.g. `["!mod", "!secret"]`. |
| `chatRelay.extraCommandsFile` | `""` | Optional text or code file. Every `"name",` or `case "name":` in it is suggested as `!name` (handy if your commands live in a Streamer.bot C# action). |
| `streamerbot.websocketUrl` | `ws://127.0.0.1:8080/` | Streamer.bot WebSocket server used by the relay. |
| `streamerbot.password` | `""` | Optional. If empty, it's read from Streamer.bot's own settings. |
| `streamerbot.settingsPath` | `auto` | Path to Streamer.bot's `data\settings.json`. `auto` checks the running Streamer.bot and `Streamer.bot*` folders in Desktop, Documents, Downloads, your user folder, `C:\`, `D:\` and Program Files. |

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
| `own` | none | Your channel names, comma-separated. |
| `host`, `port` | `127.0.0.1`, `8080` | Streamer.bot WebSocket server for reading chat. |
| `helper`, `relay` | `8767`, `8768` | Ports, if changed in config.json. |
| `demo=1` | off | Demo messages. |
