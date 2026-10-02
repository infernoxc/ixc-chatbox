# Update and uninstall

## Update
IXC checks for updates by itself. When one is out, **Home** shows **Update now**. Clicking it:
1. backs up your settings,
2. downloads the update and checks its SHA-256 fingerprint,
3. installs it and restarts IXC.

If the new version doesn't start, the previous version is put back.

You can also just run a newer `IXC-Setup.exe`. Your settings, sign-ins, phones, commands, voices and queue are kept, and backed up first.

**Coming from IXC Music 2.x or IXC ChatBox 2.x:** run the IXC 3 setup.
- Your settings are moved over.
- Passwords and keys move into the encrypted store.
- The music queue is taken over from the old player.
- The old cloudflared tunnel program is removed. The phone remote now uses the IXC relay, so scan a new QR code once.

## Uninstall
**Windows Settings > Apps > IXC > Uninstall**, or Start menu > **Uninstall IXC**. This removes:
- IXC and its "start with Windows" entry,
- the Start menu entries,
- IXC's panels in OBS (when OBS is closed),
- the **IXC Audio** scene and IXC's sources (when OBS is open while you uninstall).

To keep your settings for a later reinstall, run this from a PowerShell window:
`powershell -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\IXC-OBS\app\scripts\uninstall.ps1" -KeepSettings`

OBS itself is not changed otherwise. Its WebSocket server stays on, and OBS's settings backups (`*.before-ixc-*.bak`) stay next to its settings.
