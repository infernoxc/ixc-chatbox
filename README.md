<div align="center">

<img src="docs/images/banner.png" alt="IXC - music, chat and chat voice for OBS" width="100%">

[![Download](https://img.shields.io/github/v/release/infernoxc/ixc-chatbox?label=download&color=e3141e)](https://github.com/infernoxc/ixc-chatbox/releases/latest)
[![CI](https://github.com/infernoxc/ixc-chatbox/actions/workflows/ci.yml/badge.svg)](https://github.com/infernoxc/ixc-chatbox/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-e3141e.svg)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![OBS Studio](https://img.shields.io/badge/OBS%20Studio-30%2B-302E31)

**IXC Suite = IXC Music + IXC ChatBox.** Twitch, Kick, YouTube and Rumble chat in one place, a chat voice (TTS), a music player
with song requests, overlays, and a phone remote that works on mobile data. Made for OBS. **No Streamer.bot needed.**

[**⬇ Download IXC-Setup.exe**](https://github.com/infernoxc/ixc-chatbox/releases/latest) · [**📖 Step-by-step guide**](docs/GUIDE.md) · [Install](docs/INSTALL.md) · [How to use](docs/USAGE.md) · [Something wrong?](docs/TROUBLESHOOTING.md)

</div>

---

## Get started (5 minutes)

1. **Install OBS Studio** (free, [obsproject.com](https://obsproject.com)) if you don't have it, and open it once.
2. **Download IXC:** open [Releases](https://github.com/infernoxc/ixc-chatbox/releases/latest), scroll down to **Assets** and click
   **`IXC-Setup-v3.2.0.exe`** (the number is the version).
3. **Run it** from your Downloads folder.
   - If Windows says **"Windows protected your PC"**: click **More info**, then **Run anyway**. (IXC is free and has no paid
     signing certificate.)
   - Click **Express setup**. No admin rights needed. If OBS is open, the setup asks you to close it once.
4. **IXC opens a short setup assistant.** Just follow it:
   - it **sets up OBS** by itself (panels, music player, chat voice),
   - you type your **channel names** (Twitch, Kick, YouTube, Rumble): that's enough to read chat,
   - optional: scan a **QR code** to control IXC from your phone,
   - it **runs a system test**; anything wrong has a **Fix** button.
5. **Open OBS and stream.** The IXC panels are under **Docks**. IXC sits in the tray (bottom right) and from now on **starts by
   itself when you open OBS**.

That's all. You never edit files, ports, JSON or firewall rules.
Want IXC to also **reply** in chat (commands, timers, thank-you messages)? Follow the 5-minute steps in IXC > **Platforms &
accounts > Your apps** (one free developer app per platform). Full walkthrough: [📖 Step-by-step guide](docs/GUIDE.md).

## What you get
| | |
|---|---|
| 💬 **One chat** | Twitch, Kick, YouTube and Rumble in one panel, with emotes, badges and roles. Reply to one platform or all of them. Show or hide each platform. |
| 🔊 **Chat voice (TTS)** | Reads chat out loud through OBS. 11 natural voices, plus an offline Windows voice as a backup. Give people their own voice, or set one per role or platform. Pause, skip, queue, history. |
| 🎚 **Music ducking** | Music fades down smoothly while the voice speaks, then comes back to exactly where it was. |
| 🎵 **Music** | YouTube search and links, Spotify links (matched on YouTube), your own MP3/FLAC/… folders. Queue, shuffle, repeat, autoplay. Shuffle plays every song once before repeating. Playlists and history. |
| 🎚 **Audio routing** | Switch the **music** and the **chat voice** on or off **per platform** (for example music on Twitch and YouTube, not on Kick). Uses OBS audio tracks, needs one OBS output per platform (multistream). |
| 🙋 **Song requests** | `!sr song name` from chat, with who-can-request, cooldowns, limits, banned songs/artists and a maximum length. `!wrongsong` removes your last request. |
| ⌨️ **Commands** | `!np` `!queue` `!skip` `!uptime` `!viewers`, ready-made `!commands` `!followage` `!clip` `!discord` `!socials`, and your own (`!discord`, `!socials`…) with permissions, cooldowns, platforms, a different answer per platform and OBS actions (`!brb` → BRB scene). Import from Streamer.bot. |
| ⏱ **Timers** | Repeating messages ("Follow me everywhere…") every N minutes, only after enough chat, only while live. Never repeats after a reconnect or restart. |
| 🎉 **Event messages** | Thank-you in chat for follows, subs, gifts, raids, Super Chats and members, with flood protection. |
| 🎬 **OBS control** | Direct OBS WebSocket connection: streaming / recording state, scene switching, show / hide sources, connection test. |
| 🛡 **Moderation** | Delete, time out and ban from the chat panel through each platform's official API. |
| 🛡 **Chat filters** | Links, caps, spam, copy-paste waves, banned words, block and allow lists, "subscribers only" on stream. You still see everything in your panel. |
| 👁 **Viewer counter** | Per platform and total. A number that's out of date is never shown as current. |
| 🖼 **Overlays** | Now Playing (full / compact / minimal), Up Next, Chat, Viewers, Alerts (follows, subs, raids, Super Chats), chat-voice indicator and a "Live on" badge. Style them in IXC and press **Add to OBS**. |
| 📱 **Phone remote** | Scan a QR code once, then control music, voice and chat from anywhere: 4G/5G, another Wi-Fi, another city. It keeps working after restarts. |
| 🚀 **Starts with OBS** | Open OBS and IXC is there; close OBS and IXC closes too. Only a tiny tray icon waits in the background. |
| 🩺 **System check** | One button checks everything; **Fix automatically** repairs it. Backups, diagnostics export, safe updates with rollback. |

## Requirements
- **Windows 10 (1903 or newer) or Windows 11.** Everything else IXC needs is built into Windows.
- **OBS Studio 30 or newer** (free, [obsproject.com](https://obsproject.com)).
- **Internet** for the platforms, neural voices, YouTube and the phone remote. Local music and the offline voice work without it.
- **No Streamer.bot and no plugins needed.** IXC does chat, replies, commands, timers, event messages, chat voice, music and OBS by itself.
  To reply on Twitch, YouTube and Kick, create a free developer app once per platform with your own account (steps inside IXC, about
  5 minutes each). Streamer.bot still works as an optional bridge if you run it.

## Honest limitations
- **Neural voices** use Microsoft Edge's "Read aloud" service, which isn't an official API. If it ever stops, IXC switches to the offline Windows voice by itself and tells you.
- **Reading chat** on Kick and YouTube uses the same public connections their websites use, and these can change. Replying uses the official APIs (you sign in).
- **Followers:** follower alerts and "followers only" checks work on Twitch (signed in), Kick and Rumble. YouTube doesn't share them.
- **Spotify** audio can't be streamed. IXC plays the same song from YouTube. Spotify playlists and albums need your own free Spotify app keys (Settings, Advanced).
- **TikTok, Instagram and Facebook Live** aren't supported: TikTok has no official chat API, Instagram and Facebook need an app approved by Meta.
- **Music copyright:** use music you're allowed to stream (for example NCS or StreamBeats). Platforms mute or strike copyrighted music.

## Documentation
[Step-by-step guide](docs/GUIDE.md) · [Install](docs/INSTALL.md) · [Usage](docs/USAGE.md) · [Troubleshooting](docs/TROUBLESHOOTING.md) · [Update & uninstall](docs/UNINSTALL.md) · [All settings](docs/CONFIGURATION.md)
· For developers: [Architecture & API](docs/ARCHITECTURE.md) · [Developer setup (relay, sign-in apps)](docs/DEVELOPER-SETUP.md) · [Releasing](docs/RELEASING.md) · [Contributing & tests](CONTRIBUTING.md)

## Privacy and security
- Everything runs **on your PC**. IXC only listens on your PC's own address (`localhost`) and refuses requests from websites.
- **Sign-ins and keys are encrypted** for your Windows user and never written to settings files, logs or diagnostics.
- The **phone remote** dials *out* to a relay. Nothing on your PC is opened to the internet. Each phone gets its own key, which you can rename or remove at any time. QR codes work once and expire after 5 minutes. Details: [SECURITY.md](SECURITY.md).

## License and credits
Code © 2026 **Ishan (InFerNoxC)**, [MIT License](LICENSE). OBS Studio, Streamer.bot, Twitch, Kick, YouTube, Rumble, Spotify,
Microsoft's voices and Cloudflare belong to their owners: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Not affiliated with any of them.

<div align="center"><sub>Made by <a href="https://github.com/infernoxc">InFerNoxC</a> for multistreamers who are tired of five windows.</sub></div>
