# Update and uninstall

## Update to a new version
Close OBS and run the new Setup `.exe` or `Install.bat`. Your `config.json`, phone key and dock settings are kept.
Your version is in `%LOCALAPPDATA%\IXC-OBS\app\chat\VERSION`.

## Uninstall
1. If you turned on the phone view: run `enable-phone-access.ps1 -Disable` **as administrator** first. This removes its firewall rule and URL reservation.
2. **Close OBS**.
3. Start menu › **IXC for OBS › Uninstall IXC ChatBox**, or `Uninstall.bat` in the download folder.
   To keep your settings: `powershell -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\IXC-OBS\app\scripts\uninstall.ps1" -App chat -KeepSettings`
4. In OBS, delete the **IXC ChatBox TTS** and chat overlay sources.

This removes the ChatBox files, its relay, its dock and its Start menu entry. If [IXC Music](https://github.com/infernoxc/ixc-music)
is still installed, the shared helper, login task and settings stay. Otherwise `%LOCALAPPDATA%\IXC-OBS` (including logs and
the phone key), the login task and the Start menu folder are removed too. Setup `.exe` users can also uninstall from **Settings › Apps**.
