# IXC step-by-step guide

From nothing to a working stream setup, then where to find every feature. IXC is **IXC Music + IXC ChatBox in one program**.
You don't need Streamer.bot.

**Contents**
1. [Before you start](#1-before-you-start)
2. [Install](#2-install)
3. [The setup assistant](#3-the-setup-assistant)
4. [Where everything is](#4-where-everything-is)
5. [Connect your platforms](#5-connect-your-platforms)
6. [Chat](#6-chat)
7. [Chat voice (TTS)](#7-chat-voice-tts)
8. [Music](#8-music)
9. [Song requests](#9-song-requests)
10. [Commands and chat filters](#10-commands-and-chat-filters)
11. [Overlays](#11-overlays)
12. [Phone remote](#12-phone-remote)
13. [System check, backups, updates](#13-system-check-backups-updates)
14. [Moving from IXC Music 2.x / IXC ChatBox 2.x](#14-moving-from-ixc-music-2x--ixc-chatbox-2x)
15. [Uninstall](#15-uninstall)
16. [If something doesn't work](#16-if-something-doesnt-work)

---

## 1. Before you start
| You need | Notes |
|---|---|
| Windows 10 or 11 | .NET Framework 4.8, which Windows 10 (1903+) and 11 already have |
| OBS Studio 30 or newer | [obsproject.com](https://obsproject.com). **Open it once** after installing, then close it |
| Internet | For chat, YouTube music and the neural voices |

You don't need administrator rights, port forwarding or firewall changes.

## 2. Install
1. Go to **[Releases](https://github.com/infernoxc/ixc-chatbox/releases/latest)** and download **`IXC-Setup-vX.Y.Z.exe`**.
2. **Close OBS** (File > Exit). IXC can only add its panels to OBS while OBS is closed. If OBS is open, the setup asks you to close it.
3. Run `IXC-Setup-vX.Y.Z.exe`.
   - If Windows says **"Windows protected your PC"**, click **More info**, then **Run anyway**. IXC is free and has no paid code-signing
     certificate. You can check the file against `SHA256SUMS.txt` on the release page.
4. Choose **Express (recommended)** and click **Install**.
   - **Advanced** lets you choose whether IXC starts with Windows, whether it changes OBS's settings, and whether to add a desktop icon.
5. Click **Finish**. IXC starts, puts its icon in the **tray** (bottom right, next to the clock) and opens the setup assistant.

<details><summary>Other ways to install (ZIP, from source)</summary>

- **ZIP:** download `IXC-Suite-vX.Y.Z.zip`, unzip it, close OBS, double-click **`Install.bat`**.
- **From source:**
  ```powershell
  git clone https://github.com/infernoxc/ixc-chatbox.git
  cd ixc-chatbox
  powershell -ExecutionPolicy Bypass -File scripts\install.ps1
  ```
  IXC is built with the C# compiler that ships with Windows; nothing else is downloaded.
</details>

## 3. The setup assistant
It opens by itself the first time. You can open it again any time: **Settings > Run setup again** (or **Home > Start setup**).

| Step | What to do |
|---|---|
| **Welcome** | Shows that IXC is running. Click **Next**. |
| **1. OBS Studio** | **Open OBS**, then click **Add IXC to OBS**. IXC turns on OBS's WebSocket server and adds a scene **IXC Audio** (music player + chat voice) inside every scene, so the sound is always on stream. You don't see it on screen. |
| **2. Your platforms** | Type your channel name for Twitch, Kick, YouTube and/or Rumble. That's enough to **read** chat. Click **Connect** to sign in if you also want to **reply**, get follower alerts and Twitch viewer numbers. |
| **3. Sound check** | Click **Test voice**. You should hear it in OBS (the **IXC TTS** source in the audio mixer moves). Turn the chat voice and music ducking on or off. |
| **4. Your phone** *(optional)* | Scan the QR code with your phone camera. |
| **5. Overlays** | Click the overlays you want (Now Playing, Chat, Viewer counter, Alerts). They're added to the scene that's live in OBS. |
| **6. System test** | IXC checks everything. If something is red, click **Fix**. |

## 4. Where everything is

### The IXC window (dashboard)
Open it in any of these ways:
- double-click the IXC tray icon,
- Start menu > **IXC**,
- the **IXC** panel in OBS,
- in a browser: **http://localhost:8767/** (if 8767 was taken, IXC uses the next free port, shown in **Settings**).

The menu on the left:

| Page | What you do there |
|---|---|
| **Home** | Status of everything, viewers, now playing, chat voice on/off, **Update now** when there's an update |
| **Chat** | All chat in one list, reply, moderation |
| **Music** | Search, queue, player controls, music folders, which platforms hear the music |
| **Song requests** | Turn requests on and set their rules; the list of requests |
| **Platforms & accounts** | Channels, **Connect** / **Sign out**, connection status per platform, Streamer.bot (optional) |
| **Overlays** | Pick an overlay, style it, **Add to OBS** |
| **Phone remote** | QR code, list of paired phones |
| **Chat voice (TTS)** | Voice, speed, volume, what gets read |
| **Voices for people** | A voice per person, per role (moderators, VIPs, subscribers) or per platform |
| **Commands** | Built-in and your own chat commands |
| **Chat filters** | Links, caps, spam, banned words… |
| **System check** | Check and **Fix automatically**, repair tools, backups, diagnostics, updates |
| **Settings** | Start with Windows, updates, viewer refresh, **Advanced mode** |

### In OBS
| Where | Name | What it is |
|---|---|---|
| **Docks** menu | **IXC ChatBox** | Chat panel: read, reply, voice on/off, skip |
| **Docks** menu | **IXC Music** | Music panel: search, queue, play/pause/skip, volume, music destination |
| **Docks** menu | **IXC** | The full IXC window inside OBS |
| Scenes | **IXC Audio** | Holds **IXC Music Player** and **IXC TTS**. Added inside every scene. Don't delete it |
| Sources | Overlays you added | Browser sources named after the overlay |

If a panel is missing: **Docks** menu, tick it. If it's not listed, **System check > Repair > Add IXC panels to OBS** (with OBS closed).

### The tray icon (bottom right)
Right-click: **Open IXC**, **System check**, **Connect phone**, **Restart IXC**, **Open logs folder**, **Quit IXC**.

### Addresses (for your own browser sources or other tools)
Replace `8767` if IXC uses another port.

| Address | What |
|---|---|
| `http://localhost:8767/` | IXC window |
| `http://localhost:8767/chat/chat.html` | Chat panel |
| `http://localhost:8767/music/dock.html` | Music panel |
| `http://localhost:8767/overlay/nowplaying.html` | Now Playing overlay |
| `http://localhost:8767/overlay/queue.html` | Queue overlay |
| `http://localhost:8767/overlay/chat.html` | Chat overlay |
| `http://localhost:8767/overlay/viewers.html` | Viewer counter overlay |
| `http://localhost:8767/overlay/alerts.html` | Alerts overlay |
| `http://localhost:8767/overlay/tts.html` | "Now reading" overlay |
| `http://localhost:8767/overlay/status.html` | Connection status overlay |
| `http://localhost:8767/api/nowplaying.txt` | Current song as text |
| `http://localhost:8767/api/viewers.txt?p=total` | Viewers as text (`p=twitch`, `kick`, `youtube`, `rumble`, `total`) |

**Text files** for OBS *Text (GDI+) > Read from file*: `%LOCALAPPDATA%\IXC-OBS\output\nowplaying.txt`, `viewers.txt`, `viewers-total.txt`.

### Where IXC keeps its files
`%LOCALAPPDATA%\IXC-OBS\`: program (`app\`), settings (`config.json`), encrypted sign-ins (`secrets.dat`), music queue, `logs\`,
`backups\`, `output\`, `exports\`. **You never need to edit these files.** Everything is in the IXC window.

## 5. Connect your platforms
**Platforms & accounts** page.

| Platform | To read chat | To reply / alerts / viewers |
|---|---|---|
| **Twitch** | Type your channel name (viewer numbers work too) | **Connect** → a code is shown → enter it on twitch.tv/activate |
| **Kick** | Type your channel name | **Connect** → sign in to Kick in the browser |
| **YouTube** | Type your channel (`@name`, channel link or a live video link) | **Connect** → sign in with Google |
| **Rumble** | Paste your **Live Stream API** link (rumble.com > Account > Live Stream API) | Rumble doesn't allow apps to send messages |
| **Streamer.bot** *(optional)* | Auto: used if it's running with its WebSocket server on | – |

Each platform shows its own status, such as **Connected**, **Reconnecting…**, **Not live**, **Offline** or **Sign-in required**. If one platform has a
problem, the others keep working. YouTube chat appears when you go live; IXC checks every 30 seconds.

## 6. Chat
In the **IXC ChatBox** panel or the **Chat** page:
1. Messages from all platforms appear in one list, tagged **TWITCH**, **KICK**, **YT**, **RUMBLE**. Click a tag at the top to hide or show that platform.
2. **Reply:** type at the bottom. The button on the left picks where it goes: **ALL** or one platform. Type `!` for every command (IXC's and
   Streamer.bot's, each labelled), `@` for names. Replies go out with your signed-in account, or through **Streamer.bot** when it's running
   and you haven't signed in to that platform in IXC.
3. The top bar shows **👁 watching** (viewers on all platforms right now) and **💬 chatting** (people who wrote in the last 10 minutes).
4. **Click a message** to reply to that person, choose a voice for them, never read them out loud, hide them everywhere, or copy the message.
   Messages deleted by moderators on the platform disappear from IXC too.
5. Messages caught by a filter stay in your panel, dimmed with the reason, but are hidden on stream.

## 7. Chat voice (TTS)
1. Switch it on: **🔈** in the chat panel, **Home**, or your phone.
2. **⏸** pauses (messages wait), **Skip** stops the current message.
3. **Chat voice (TTS)** page: default voice, speed, pitch, volume; read mode (all messages / only `!tts`); platforms; who is read; emotes,
   emoji, links; busy-chat limits; never-read people and words.
4. **Voices for people:** add a person by name (or click their message in chat > **Choose a voice**), pick a voice, speed and volume. Or set a voice for moderators, VIPs, subscribers, or a whole platform.
5. If the neural voices are unavailable, IXC switches to the offline Windows voice by itself.

Moderators can type `!ttsskip`, `!ttson`, `!ttsoff` (you can turn this off).

## 8. Music
1. In the **IXC Music** panel (or **Music** page) search, or paste a YouTube / YouTube Music / Spotify link (song, playlist or album).
   **Enter** adds to the queue, **Shift+Enter** plays now.
2. **Your own songs:** **Music > Add a music folder**. MP3, M4A, AAC, OGG, OPUS, WAV, FLAC and WEBM files appear in search like YouTube songs.
3. **Music goes to:** **ALL**, **TWITCH**, **KICK**, **YOUTUBE** or **OFF**. IXC switches the OBS audio tracks of the music player and
   checks with OBS. If you multistream with a separate audio track per platform, set the track per platform under
   **Music > Which platform hears the music**.
4. **Ducking:** while the chat voice speaks, the music fades down to the level you choose and back up after. Your normal volume never changes.
5. Shuffle, repeat, autoplay similar songs, and **Start music when OBS opens** (continues where it stopped).
6. The queue is kept by IXC: restarting OBS, IXC or the PC loses nothing.

Spotify playlists/albums need a free Spotify developer app: turn on **Settings > Advanced mode**, then enter the **Spotify app Client ID** and **Client Secret**.

## 9. Song requests
1. **Song requests** page > turn **On**.
2. Viewers type `!sr song name` or `!sr <YouTube link>` (also `!song`).
3. Set: who can request, platforms, cooldowns, max waiting (total and per person), longest song, minimum Twitch account age,
   links and/or song names allowed, banned songs and artists, answer in chat.
4. Requests show who asked. Play or remove them on the **Song requests** page, the music panel or your phone.
5. Viewers remove their own last request with `!wrongsong`.

## 10. Commands and chat filters
**Commands** page: built-in `!np`, `!queue`, `!skip`, `!uptime`, `!wrongsong`, `!ttsskip`, `!ttson`, `!ttsoff`, `!viewers`.
Turn each on/off, edit the answer, or click **+ New command**:

| Field | |
|---|---|
| Name / other names | e.g. `!discord`, `!dc` |
| Answer | Text with `{user} {song} {queue} {uptime} {viewers} {count} {args}` |
| Who | Everyone, followers, subscribers, VIPs, moderators, only you |
| Cooldown | For everyone and per person (moderators skip cooldowns) |
| Platforms | Where it works |

**Chat filters** page: links, caps, repeated characters, long messages, repeating people, copy-paste spam, weird characters, banned words
(`*` wildcard), always-hide / never-filter people. Each filter: **Off**, **Don't read it out**, or **Hide on stream**.

## 11. Overlays
1. **Overlays** page > pick one: Now Playing, Queue, Chat, Viewer counter, Chat voice, Alerts, Status.
2. Style it: layout, contents, font, size, colors, background, corners, alignment, animation. The preview updates live.
3. Select the scene in OBS, then click **Add to OBS (current scene)**. Move and resize it in OBS like any source.
4. Later style changes apply live; no need to re-add it.

Overlays are transparent. If IXC restarts, they keep showing the last state and reconnect by themselves.

## 12. Phone remote
No app, no account, no settings.

1. **Phone remote** page (or tray > **Connect phone**) > **Show QR code**.
   - The first time, IXC downloads Cloudflare's free tunnel program (about 60 MB, once) and opens a temporary secure link.
     This takes a few seconds; the QR code appears by itself.
2. Scan it with the phone camera and open the link. The code works **once** and for **5 minutes**.
3. Done. It works on mobile data and any Wi-Fi.

**Quick connect is temporary:** the link closes when IXC closes, or after 30 minutes without a phone. Next time, press
**Show QR code** again and scan the new code. **Stop Quick connect** closes it right away.

*Permanent link (optional):* if a phone relay is set up ([developer setup](DEVELOPER-SETUP.md), or **Settings > Advanced mode >
Phone relay address**), **Show QR code** uses it instead: phones then stay paired across restarts. **Use Quick connect instead** is
still there.

| Phone tab | |
|---|---|
| **Music** | Play/pause, skip, volume, queue, add songs, music destination |
| **Chat** | Chat, watching / chatting counts, platform filter, replies, `!` command list |
| **Voice** | On/off, pause, skip, voice, volume, platforms |
| **Status** | Viewers, connections, latency |

The phone reconnects by itself after network changes, sleep, or an IXC/PC restart. On the PC, **Phone remote** lists your phones:
**Rename**, **Disconnect**, **Remove**, or **Remove all**. Lost a phone? **Remove** it. A removed phone is disconnected at once.

## 13. System check, backups, updates
- **System check > Run check:** OBS, IXC sources, music audio, chat voice, each platform, sign-ins, phone relay, internet, settings, updates.
  Red items have **Fix automatically**.
- **Repair** tools: **Reconnect everything**, add IXC to OBS again, **Add IXC panels to OBS**, **Reload all overlays and panels**, repair the voice,
  **Clear caches**, reset settings (keeps your port and addresses).
- **Backups:** **Back up now** and **Restore**. IXC also backs up before every update.
- **Export diagnostics:** a ZIP to send when asking for help. No passwords, keys, sign-ins or IP addresses inside.
- **Updates:** when **Home** shows **Update now**, click it. IXC backs up, downloads, checks the file's fingerprint, installs and restarts.
  If the new version doesn't start, the old one comes back by itself. You can also just run a newer `IXC-Setup.exe`; settings are kept.

## 14. Moving from IXC Music 2.x / IXC ChatBox 2.x
Close OBS and run the IXC 3 setup. That's all:
- settings move over and are backed up first; passwords and keys move into the encrypted store;
- the music queue is taken over from the old player;
- the old cloudflared tunnel program is removed. **Phones scan a new QR code once.**
- The panels are now **IXC ChatBox**, **IXC Music** and **IXC**. If you had added the old URLs yourself as browser sources, they keep working.

## 15. Uninstall
**Windows Settings > Apps > IXC > Uninstall** (or Start menu > **Uninstall IXC**). It removes IXC, its startup entry, the Start menu
entries, IXC's panels in OBS (when OBS is closed) and the IXC Audio scene and sources (when OBS is open). Details and how to keep your
settings: [UNINSTALL.md](UNINSTALL.md).

## 16. If something doesn't work
1. **System check > Run check**, then **Fix automatically**.
2. Still wrong? Tray > **Restart IXC**.
3. Still wrong? See [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
4. Ask for help with **System check > Export diagnostics** attached:
   [open an issue](https://github.com/infernoxc/ixc-chatbox/issues/new).
