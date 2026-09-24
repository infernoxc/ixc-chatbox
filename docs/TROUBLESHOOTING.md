# Troubleshooting

**First check:** open **http://localhost:8767/diag**. It shows what's connected, what's failing and the recent events.
If it doesn't open, IXC isn't running: Start menu › IXC for OBS › **Start IXC**. Log: `%LOCALAPPDATA%\IXC-OBS\ixc-core.log`.

| Problem | Fix |
|---|---|
| Diagnostics don't open / `IXC Core running: NO` | Another program uses port 8767. If you used the v1 scripts from an old download, stop them. Or change `helper.port`, restart IXC and change the OBS URLs. |
| "IXC Core could not be built" | Needs .NET Framework 4.8 (built into Windows 10 1903+ and 11). Run Windows Update, then install again. |
| Dock shows **CHAT RECONNECTING** | Streamer.bot isn't running, or its WebSocket server isn't started on `127.0.0.1:8080` (Servers/Clients › WebSocket Server › Start Server, Auto Start on). |
| Twitch works, Kick or YouTube doesn't | Connect that platform in Streamer.bot › Platforms. YouTube chat only exists while you're live. |
| **"✗ not sent: Authentication required"** | Enable Authentication in Streamer.bot's WebSocket server and set a password. Diagnostics › Chat › Authentication should say `ok`. |
| TTS doesn't speak | Diagnostics › Text-to-speech: TTS must be **ON** and "TTS sources in OBS" at least **1**. If it's 0, add `/chat/tts.html` as a browser source. The dock's ⚙ › *Not read (and why)* shows why a message was skipped. |
| TTS doesn't read my own messages | That's intended (*Ignore my own account*). Turn it off in ⚙ if you want. |
| TTS: viewers don't hear it | The *IXC ChatBox TTS* source needs **Control audio via OBS** and the right tracks (Advanced Audio Properties). |
| TTS says "offline Windows voice (neural voice failed …)" | The Microsoft service didn't answer (internet, or the service changed). IXC retries it after 60 s. Test with the **Test** button. |
| TTS in a normal browser tab doesn't play | Browsers block sound until you click the page once; the TTS tab shows a button for it. OBS doesn't need this. |
| Phone: QR window says it couldn't download cloudflared | Check your internet or firewall. Or download `cloudflared-windows-amd64.exe` from Cloudflare's GitHub yourself and set `remote.cloudflaredPath`. |
| Phone: "This QR code has expired or was already used" | Codes work once, for 5 minutes. Press **New code**. |
| Phone: "signed out" | IXC restarted, the tunnel stopped after 30 idle minutes, or phones were signed out. Scan a new QR code. |
| Phone: "too many wrong codes" | 5 wrong tries lock pairing from that network for 10 minutes. Wait, then scan a fresh code. |
| "running scripts is disabled on this system" | Use `Install.bat`, or `powershell -ExecutionPolicy Bypass -File scripts\install.ps1`. |

Still stuck? [Open an issue](https://github.com/infernoxc/ixc-chatbox/issues/new/choose) and paste the **Recent events** from the diagnostics page.
Never include your config.json or passwords.
