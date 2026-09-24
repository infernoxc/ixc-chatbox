<div align="center">

<img src="docs/images/banner.png" alt="IXC ChatBox - Twitch + Kick + YouTube chat in one OBS dock" width="100%">

[![Download](https://img.shields.io/github/v/release/infernoxc/ixc-chatbox?label=download&color=e3141e)](https://github.com/infernoxc/ixc-chatbox/releases/latest)
[![CI](https://github.com/infernoxc/ixc-chatbox/actions/workflows/ci.yml/badge.svg)](https://github.com/infernoxc/ixc-chatbox/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-e3141e.svg)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![OBS Studio](https://img.shields.io/badge/OBS%20Studio-30%2B-302E31)
![Streamer.bot](https://img.shields.io/badge/Streamer.bot-1.0%2B-5b3fd1)

**Twitch, Kick and YouTube chat in one OBS dock, with replies, neural text-to-speech, an on-stream overlay and an optional phone view.**

[**⬇ Download the latest release**](https://github.com/infernoxc/ixc-chatbox/releases/latest) · [Install guide](docs/INSTALL.md) · [How to use](docs/USAGE.md) · [Troubleshooting](docs/TROUBLESHOOTING.md)

</div>

---

<img src="docs/images/chatbox-dock.png" alt="IXC ChatBox dock with Kick, Twitch and YouTube messages" align="right" width="330">

## Features
- 💬 **One chat for every platform:** Twitch, Kick and YouTube in one list, each message tagged **TWITCH**, **KICK** or **YT**, with a coloured stripe.
- 😀 **Emotes, badges and avatars:** Twitch emotes, Kick `[emote:…]` codes, YouTube emojis, and Kick and YouTube profile pictures (Twitch shows initials).
- 📊 **Live viewer counts** per platform, plus the total.
- 🧷 **Dock mode:** auto-follows new messages, and a "↓ new messages" button appears if you scroll up.
- 📺 **Overlay mode:** a transparent on-stream chat where old messages fade out.
- ✍️ **Reply box:** send to **All**, Twitch, Kick or YouTube. Type `!` for your bot commands and `@` for recent chatters; click any message to reply to that person.
- 🔊 **Neural TTS:** 5 voices (Indian English male/female, US English male/female, a "Jarvis"-style British voice), played through an OBS source so viewers hear it. It skips `!commands`, links and bots.
- 📱 **Phone view** *(optional, off by default)*: scan a QR code and chat from your phone over your home Wi-Fi.

<br clear="right">

## Requirements
| | |
|---|---|
| Windows | 10 or 11 (Windows PowerShell 5.1 and .NET Framework 4.8 are built in) |
| OBS Studio | 30 or newer |
| [Streamer.bot](https://streamer.bot) | 1.0 or newer (free), connected to your platforms, with its **WebSocket Server** on |
| Internet | For the platforms, TTS voices and emote images |
| Tested on | Windows 11 (build 26200), OBS Studio 32.2.2, Streamer.bot 1.0.7. Other versions aren't tested yet. [Tell us](https://github.com/infernoxc/ixc-chatbox/issues) if they work for you. |

## Install
1. Download **`IXC-ChatBox-Setup-vX.Y.Z.exe`** (installer) or **`IXC-ChatBox-vX.Y.Z.zip`** (portable) from [Releases](https://github.com/infernoxc/ixc-chatbox/releases/latest).
2. Close OBS, then run the installer (or unzip and double-click **`Install.bat`**). No admin rights needed.
3. In Streamer.bot: **Servers/Clients › WebSocket Server** › Auto Start, `127.0.0.1:8080`, **Start Server**. To enable replies, turn on authentication.
4. In OBS:

| Add in OBS | URL |
|---|---|
| Docks › Custom Browser Docks: **IXC ChatBox** | `http://localhost:8767/chat/chat.html?dock=1&viewers=1&own=YOURNAME` |
| Browser source **IXC ChatBox TTS** (64×64, ✅ Control audio via OBS, FPS 1) | `http://localhost:8767/chat/tts.html` |
| Browser source for the on-stream chat *(optional)* | `http://localhost:8767/chat/chat.html?max=6&fade=45` |

Full step-by-step guide: **[docs/INSTALL.md](docs/INSTALL.md)**. Want to try it without Streamer.bot? Open
`http://localhost:8767/chat/chat.html?dock=1&demo=1`.

## Documentation
[Install](docs/INSTALL.md) · [Usage](docs/USAGE.md) · [Configuration](docs/CONFIGURATION.md) · [Troubleshooting](docs/TROUBLESHOOTING.md) · [Update and uninstall](docs/UNINSTALL.md) · [Architecture and API](docs/ARCHITECTURE.md) · [Releasing](docs/RELEASING.md)

## Build from source
```powershell
git clone https://github.com/infernoxc/ixc-chatbox.git
cd ixc-chatbox
powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -Online     # tests
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -AddObsDocks -ChannelNames "yourtwitch,yourkick"
powershell -ExecutionPolicy Bypass -File build\build.ps1 -Installer  # dist\ ZIP + Setup.exe (needs Inno Setup 6)
```

## Privacy and security
- Runs **only on your PC**. The helper and relay listen on `localhost`, and the API refuses requests from other websites.
- **No accounts, API keys or tokens.** Replies go through your own Streamer.bot. Its WebSocket password is read on your PC and never sent to any web page.
- The phone view is **off by default**. If you enable it, it works on private networks only, protected by a random key. More in [SECURITY.md](SECURITY.md).

## Works great with
**[IXC Music](https://github.com/infernoxc/ixc-music)**: a YouTube music player and dock for OBS. Both share the same small helper.

## License and credits
Code © 2026 **Ishan (InFerNoxC)**, released under the [MIT License](LICENSE).
Streamer.bot, OBS Studio, Twitch, Kick, YouTube and the Microsoft neural voices belong to their owners and have their own
terms: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Not affiliated with or endorsed by any of them.

<div align="center"><sub>Made by <a href="https://github.com/infernoxc">InFerNoxC</a> for multistreamers who are tired of three chat windows.</sub></div>
