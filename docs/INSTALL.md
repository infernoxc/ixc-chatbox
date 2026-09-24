# Install IXC ChatBox

About 10 minutes. No programming and no admin rights needed.

## 1. Before you start
- Windows 10 or 11, and **OBS Studio 30+** ([obsproject.com](https://obsproject.com)).
- **[Streamer.bot](https://streamer.bot)** (free), with your accounts connected under **Platforms** (Twitch / Kick / YouTube).
  Streamer.bot does the logging in to each platform; ChatBox only talks to Streamer.bot.

## 2. Download
From the [latest release](https://github.com/infernoxc/ixc-chatbox/releases/latest):
- **`IXC-ChatBox-Setup-vX.Y.Z.exe`** (installer), or
- **`IXC-ChatBox-vX.Y.Z.zip`** (portable package; unzip it anywhere).

> The files aren't code-signed yet, so Windows SmartScreen may warn you. Click **More info › Run anyway**, but only for files from
> the official Releases page. Verify with `Get-FileHash <file> -Algorithm SHA256` against `SHA256SUMS.txt`.

## 3. Install
Close OBS, then run the Setup `.exe` or double-click **`Install.bat`**. You should see `IXC Core running: YES`.

The installer:
- builds **IXC Core** on your PC with the C# compiler built into Windows (a few seconds; the release contains source code, not a program file);
- installs to `%LOCALAPPDATA%\IXC-OBS`;
- starts it at Windows login;
- adds **Start menu › IXC for OBS**.

Optional, to add the dock to OBS automatically (OBS closed):
```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -AddObsDocks
```

## 4. Turn on Streamer.bot's WebSocket server
Streamer.bot › **Servers/Clients › WebSocket Server**:
1. ✅ **Auto Start**. Address `127.0.0.1`, Port `8080`. Click **Start Server**.
2. For the **reply box**: ✅ **Enable Authentication** and set a password. IXC finds the password in Streamer.bot's own settings by itself.

## 5. Add it to OBS
### The dock
**Docks › Custom Browser Docks...**, Dock Name `IXC ChatBox`, URL:
```
http://localhost:8767/chat/chat.html?dock=1&viewers=1
```

### TTS source (so viewers hear text-to-speech)
1. **Sources › + › Browser**, name **IXC ChatBox TTS**, URL `http://localhost:8767/chat/tts.html`, 64 × 64, custom FPS `1`, ✅ **Control audio via OBS**.
2. Drag it off the canvas. In Audio Mixer › ⚙ › **Advanced Audio Properties**, choose **Monitor and Output** and the **tracks** of the platforms that should hear it.
3. Add it to every scene with *Paste (Reference)*.

### On-stream chat (optional)
**Browser** source, URL `http://localhost:8767/chat/chat.html?max=6&fade=45`, 400 × 460, placed where you want chat on screen.

## 6. First test
- Open `http://localhost:8767/diag`. Streamer.bot should show **connected** and "TTS sources in OBS" should show **1**.
- Type in your own Twitch or Kick chat. It appears in the dock with a platform tag. It is **not** read aloud, because it's your own account (you can change that in ⚙).
- Click **TTS OFF** (it turns red and shows ON), then **Test**. You and your stream hear the voice.
- Type a reply, pick **ALL** or a platform, and press Enter. A small "✓ sent to ..." note confirms it.
- Press **📱** and scan the code with your phone on mobile data. The phone shows your chat live.

Next: [Usage](USAGE.md) · [Troubleshooting](TROUBLESHOOTING.md)
