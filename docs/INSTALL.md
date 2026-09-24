# Install IXC ChatBox

About 10 minutes. No programming and no admin rights needed.

## 1. Before you start
- Windows 10 or 11, and **OBS Studio 30+** ([obsproject.com](https://obsproject.com)). Any OBS install location works.
- **[Streamer.bot](https://streamer.bot)** (free), with your accounts connected under **Platforms** (Twitch / Kick / YouTube).
  Streamer.bot does the logging in to each platform; ChatBox only talks to Streamer.bot.

## 2. Download
From the [latest release](https://github.com/infernoxc/ixc-chatbox/releases/latest):
- **`IXC-ChatBox-Setup-vX.Y.Z.exe`**: installer, or
- **`IXC-ChatBox-vX.Y.Z.zip`**: portable package; unzip it anywhere.

> The files aren't code-signed yet, so Windows SmartScreen may warn you. Click **More info › Run anyway**, only for files from
> the official Releases page. Verify with `Get-FileHash <file> -Algorithm SHA256` against `SHA256SUMS.txt`.

## 3. Install
Close OBS, then run the Setup `.exe`, or double-click **`Install.bat`**. You should see `Helper running: YES`.
It installs to `%LOCALAPPDATA%\IXC-OBS`, starts at Windows login, and adds **Start menu › IXC for OBS**.

Optional, from a PowerShell window in the unzipped folder:
```powershell
# add the dock to OBS automatically (OBS closed) and set your channel names (TTS won't read your own bot's replies)
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -AddObsDocks -ChannelNames "yourtwitch,yourkick,youryoutube"
```

## 4. Turn on Streamer.bot's WebSocket server
Streamer.bot › **Servers/Clients › WebSocket Server**:
1. ✅ **Auto Start**. Address `127.0.0.1`, Port `8080`. Click **Start Server**.
2. For the **reply box**: ✅ **Enable Authentication** and set a password. Leave the option that *enforces*
   authentication for every client **off**, so the dock and overlay can read chat. ChatBox finds the password in Streamer.bot's
   own settings by itself.

## 5. Add it to OBS
### The dock
**Docks › Custom Browser Docks...**, Dock Name `IXC ChatBox`, URL:
```
http://localhost:8767/chat/chat.html?dock=1&viewers=1&size=14&own=YOURNAME
```
Replace `YOURNAME` with your channel name(s), comma-separated, then click **Apply**.

### TTS source (so viewers hear text-to-speech)
1. **Sources › + › Browser**, name **IXC ChatBox TTS**, URL `http://localhost:8767/chat/tts.html`, 64 × 64, custom FPS `1`, ✅ **Control audio via OBS**.
2. Drag it off the canvas. Audio Mixer › ⚙ › Advanced Audio Properties › **Monitor and Output**.
3. Add it to every scene with *Paste (Reference)*.

### On-stream chat (optional)
**Browser** source, URL `http://localhost:8767/chat/chat.html?max=6&fade=45`, 400 × 460, placed where you want chat on screen.

## 6. First test
- Demo without Streamer.bot: open `http://localhost:8767/chat/chat.html?dock=1&demo=1` in a browser.
- Type in your own Twitch or Kick chat, and it appears in the dock with a platform tag.
- Click **TTS OFF** (it turns red and shows ON), then **Test**. You and your stream hear the voice.
- Type a reply, pick **ALL** or a platform, and press Enter. A small "✓ sent to ..." note confirms it.

Next: [Usage](USAGE.md) · [Troubleshooting](TROUBLESHOOTING.md)
