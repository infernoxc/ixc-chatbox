# Troubleshooting

**First checks:** http://localhost:8767/api/ping should show `{"ok":true,"app":"ixc-helper",...}`, and http://localhost:8768/api/ping
should show `"app":"ixc-chat-relay"` and `"streamerbot":true`. If not: Start menu › IXC for OBS › **Start IXC**.
Logs: `%LOCALAPPDATA%\IXC-OBS\helper.log` and `chat-relay.log`.

| Problem | Fix |
|---|---|
| Dock shows **CHAT RECONNECTING** | Streamer.bot isn't running, or its WebSocket server isn't started on `127.0.0.1:8080` (Servers/Clients › WebSocket Server › Start Server, Auto Start on). |
| Twitch works, but Kick or YouTube doesn't | Connect that platform in Streamer.bot › Platforms. YouTube chat only exists while you're live. |
| Chat is empty although Streamer.bot works | If authentication is **enforced** for every client in Streamer.bot, the dock can't read chat. Keep authentication enabled but not enforced. |
| **"✗ not sent: Authentication required"** | Enable Authentication in Streamer.bot's WebSocket server and set a password. If Streamer.bot lives somewhere unusual, set `streamerbot.settingsPath` (or `streamerbot.password`) in config.json. Then restart IXC. |
| **"✗ chat relay not running"** | Start menu › Start IXC. Check `chat-relay.log`. |
| YouTube reply fails | YouTube only accepts messages during a live broadcast. |
| TTS: viewers don't hear it | Add the **IXC ChatBox TTS** source (`/chat/tts.html`) with *Control audio via OBS* on and the right tracks. Press **Test**. |
| TTS: robotic voice that only you hear | The neural voice service couldn't be reached, so it fell back to Windows' voice. Check your internet. The service is unofficial and may change (see THIRD-PARTY-NOTICES). |
| TTS reads your own bot's replies | Add your channel names to the dock URL: `&own=name1,name2`. |
| Phone: **WRONG / MISSING KEY** | Scan the QR code again (📱 in the dock). The key changes if `phone_key.txt` is deleted. |
| Phone can't open the page | Run `enable-phone-access.ps1` as administrator, set the Windows network to **Private**, and put the phone on the same Wi-Fi. |
| QR shows "Phone access is OFF" | See the phone steps in [USAGE.md](USAGE.md#phone-view-optional). |
| `Helper running: NO` after install | Another app uses port 8767. Change `helper.port`, restart IXC, and add `&helper=<port>` to the URLs. |
| "running scripts is disabled on this system" | Use `Install.bat`, or `powershell -ExecutionPolicy Bypass -File scripts\install.ps1`. |

Still stuck? [Open an issue](https://github.com/infernoxc/ixc-chatbox/issues/new/choose). Never include your config.json, phone key or passwords.
