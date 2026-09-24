# Using IXC ChatBox

## Dock vs overlay
| Mode | URL | What you get |
|---|---|---|
| **Dock** (for you) | `http://localhost:8767/chat/chat.html?dock=1&viewers=1&size=14&own=YOURNAME` | Scrollable list that follows new messages ("↓ new messages" if you scroll up), viewer counts, TTS bar, reply box |
| **Overlay** (for viewers) | `http://localhost:8767/chat/chat.html?max=6&fade=45` | Transparent, newest at the bottom, messages fade after `fade` seconds |
| **Demo** | add `&demo=1` | Fake messages, for trying the look without Streamer.bot |

All URL options are in [CONFIGURATION.md](CONFIGURATION.md#url-options).

## Replying
- The coloured button left of the text box chooses the target: **ALL**, then **TWITCH**, **KICK**, **YT**. Click it to switch.
- **Enter** sends. A note confirms, for example "✓ sent to TWITCH + KICK". YouTube only accepts messages while you're live.
- Type **`!`** to get your bot commands (from Streamer.bot's command list, plus an optional extra file), or **`@`** to get
  recent chatters with their platform. Choose with ↑ ↓ and Tab / Enter.
- **Click a message** to reply to that person: it switches to their platform and types `@name` for you.
- Messages are sent **as your broadcaster account** through Streamer.bot.

## Text-to-speech
- In the dock: **TTS ON/OFF**, voice (Indian male, Indian female, US male, US female, Jarvis-style), what to read (*read all*,
  *name + message*, *only !tts* messages), **Test**, and **Skip** (stops the current message).
- The voices are Microsoft neural voices, played by the *IXC ChatBox TTS* browser source, so viewers hear them. If that
  service can't be reached, the dock falls back to a Windows voice that only you hear.
- It never reads `!commands`, links, known bots (Streamer.bot, Nightbot, StreamElements, Botrix, KickBot), or your own
  bot/timer messages. Put your names in `own=` so it recognises them.
- Voices can be changed in `config.json › tts.voices`.

## Phone view (optional)
Read and answer chat on your phone while you play, over your home Wi-Fi only.
1. Run **`scripts\enable-phone-access.ps1` as administrator** (it's also in `%LOCALAPPDATA%\IXC-OBS\app\scripts`). It opens the
   relay port on **private** networks only.
2. Make sure your Windows network is **Private**: Settings › Network & internet › your connection › Network profile type.
3. In the dock press **📱** and scan the QR code with your phone (same Wi-Fi).

The link contains a private key, so anyone who has it can type in your chat. To make a new key, delete
`%LOCALAPPDATA%\IXC-OBS\phone_key.txt` and restart IXC. Turn the phone view off again with `enable-phone-access.ps1 -Disable` (as administrator).
