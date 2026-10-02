# Using IXC

Open IXC from the tray icon (bottom right) or the Start menu. In OBS, the **IXC Chat**, **IXC Music** and **IXC** panels are under **Docks**.

## Chat
- All platforms in one list, tagged **TWITCH**, **KICK**, **YT** and **RUMBLE**. Click a platform name at the top to hide or show it in your panel.
- **Reply:** type at the bottom. The button on the left chooses where it goes: **ALL**, or one platform. Type `!` to get command suggestions and `@` to get name suggestions.
- **Click a message** to:
  - reply to that person,
  - give them their own voice,
  - never read them out loud,
  - hide them everywhere.
- Messages your filters caught stay visible in your panel, dimmed, with the reason. They're only hidden on stream.
- To reply from IXC, connect that platform's account (**Platforms & accounts**). Rumble doesn't allow apps to send chat messages.

## Chat voice (TTS)
- Switch it on with **🔈 Voice off/on** in the chat panel, on your phone, or on the dashboard's Home page.
- **⏸** pauses it (messages wait), **Skip** stops the current message.
- **Chat voice** page:
  - default voice, speed, pitch and volume;
  - what gets read: platforms, roles, emotes, emoji, links;
  - busy-chat limits;
  - lists of people and words never to read.
- **Voices for people:** give regulars their own voice, speed and volume. You can also set a voice for moderators, VIPs, subscribers, or a whole platform.
- **Moderators** can type `!ttsskip`, `!ttson` and `!ttsoff` (you can turn this off).
- **`!tts message`** is read first. Read mode **"Only !tts"** reads nothing else.

## Music
- Search, or paste a YouTube or Spotify link (song, playlist or album), in the **IXC Music** panel or under **Music**.
  - Press **Enter** to add to the queue.
  - Press **Shift+Enter** to play now.
- **Your own songs:** **Music > Add a music folder**. They show up in search like YouTube songs.
- **Music goes to:** **ALL**, **TWITCH**, **KICK**, **YOUTUBE** or **OFF**. IXC switches which OBS audio tracks carry the music, then checks with OBS that it worked.
  - If you multistream with separate audio tracks per platform, tell IXC which track each platform uses (**Music > Which platform hears the music**).
- **Ducking:** music fades down to the level you choose while the voice speaks, then fades back up. Your normal volume is never changed.
- **Start music when OBS opens** continues where it stopped.
- The queue is kept by IXC. Restarting OBS, IXC or the PC loses nothing.

## Song requests
1. Turn them on under **Song requests**.
2. Viewers type `!sr song name` or `!sr <YouTube link>`. `!song` works too, and you can add your own commands.
3. Choose who can request, the cooldowns, the maximum number waiting (in total and per person), the longest song allowed, and banned songs and artists.
4. Requests show who asked. Remove or play them from the **Song requests** page, the music panel or your phone.
5. Viewers can remove their own last request with `!wrongsong`.

## Commands
**Commands** page: turn built-in commands on or off, edit their answers, and add your own.

| Option | What it does |
|---|---|
| **Who** | Everyone, followers, subscribers, VIPs, moderators, or only you |
| **Cooldown** | For everyone, and per person |
| **Platforms** | Which platforms the command works on |
| **Answer** | Can use `{user} {song} {queue} {uptime} {viewers} {count} {args}` and more |

## Overlays
1. Open **Overlays** and pick one.
2. Style it: layout, font, colors, size, animation, what to show. The preview updates as you go.
3. Press **Add to OBS (current scene)**. Style changes still apply live after that.

Overlays are transparent and keep showing their last state if IXC restarts. They come back by themselves.

**Text files:** `%LOCALAPPDATA%\IXC-OBS\output\` contains `nowplaying.txt`, `viewers.txt` and `viewers-total.txt`. Use them in OBS **Text (GDI+) > Read from file**, or in other tools.

## Phone remote
1. Click **Phone remote > Show QR code** and scan it with your phone's camera. The browser opens and connects.
2. That's it. It works on mobile data and any Wi-Fi.
3. Add the page to your home screen for one-tap access.

| Tab | Controls |
|---|---|
| **Music** | Play/pause, skip, volume, queue, add songs, music destination |
| **Chat** | Chat, platform filter, replies |
| **Voice** | On/off, pause, skip, voice, volume, platforms |
| **Status** | Viewers, connections, latency |

The phone reconnects by itself after:
- network changes,
- the phone sleeping,
- IXC or the PC restarting.

On the PC, **Phone remote** lists your phones. You can rename, disconnect or remove each one, or remove all of them.

## Viewers
- **Home** shows the total and each platform. A platform with no recent number shows **–**, never an old number.
- Twitch viewer numbers need a Twitch sign-in. Kick, YouTube and Rumble don't.
- Overlay: **Viewer counter**. Text: `viewers.txt`. Web: `http://localhost:8767/api/viewers.txt?p=total`.

## System check
**System check > Run check** tests:
- OBS, the IXC sources, music audio and the chat voice,
- each platform and your sign-ins,
- the phone relay, internet, settings and updates.

Anything wrong has a **Fix automatically** button. Below the check you'll find:
- **Repair** tools,
- **Backups** (restore with one click),
- **Export diagnostics**: a ZIP for support, with no passwords or keys in it,
- **Recent events**.
