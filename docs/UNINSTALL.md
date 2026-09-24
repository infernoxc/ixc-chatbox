# Update and uninstall

## Update to a new version
Close OBS and run the new Setup `.exe` or `Install.bat`. Your `config.json` is kept (a backup is saved next to it), and IXC Core is rebuilt.
Your version is in `%LOCALAPPDATA%\IXC-OBS\app\chat\VERSION`.

**From v1.x to v2:**
- **OBS URLs stay the same.** In the dock URL you can remove `&own=...`: v2 finds your own accounts from Streamer.bot, and the first time the dock opens it moves your v1 names into the settings.
- The v1 TTS switch, voice and read mode are also taken over once.
- The v1 **phone view** (home Wi-Fi only) is replaced by the phone remote, which works everywhere. If you had turned the v1 phone view on, remove its firewall rule: run `scripts\enable-phone-access.ps1 -Disable` **from the v1 download** as administrator, or delete the rule *"IXC ChatBox - phone chat"* in Windows Defender Firewall.

## Uninstall
1. **Close OBS**.
2. Start menu › **IXC for OBS › Uninstall IXC ChatBox**, or `Uninstall.bat` in the download folder. Setup `.exe` users can also use **Settings › Apps**.
   To keep your settings: `powershell -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\IXC-OBS\app\scripts\uninstall.ps1" -App chat -KeepSettings`
3. In OBS, delete the **IXC ChatBox TTS** and chat overlay sources.

This removes ChatBox, its dock and its Start menu entry. If [IXC Music](https://github.com/infernoxc/ixc-music) is still installed, IXC Core,
the login task and your settings stay. Otherwise `%LOCALAPPDATA%\IXC-OBS` (including logs and the downloaded `cloudflared.exe`),
the login task and the Start menu folder are removed too.
