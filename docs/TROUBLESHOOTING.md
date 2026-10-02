# Something's not right?

**First:** open IXC (tray icon) and go to **System check > Run check**. Most problems have a **Fix automatically** button.
If that doesn't help, click **Export diagnostics** and attach the ZIP when you ask for help. It contains no passwords, keys or sign-ins.

| Problem | What to do |
|---|---|
| **IXC isn't in the tray** | Start menu > **IXC**. If it closes again, it restarts by itself up to 3 times. After that, the tray shows "IXC stopped unexpectedly". Click it to restart. |
| **"OBS's WebSocket server is turned off"** | Close OBS (File > Exit), then **System check > Fix automatically**, then open OBS. |
| **"OBS refused the WebSocket password"** | **System check > Repair OBS connection** (with OBS closed). |
| **No music sound on stream** | **System check > Repair OBS sources**. In OBS, the scene **IXC Audio** must be inside your scenes. IXC adds it to every scene. |
| **Music only on some platforms / wrong platform** | Check **Music goes to** and, under **Music**, which track each platform uses. That has to match your OBS output or multistream plugin. |
| **No chat voice on stream** | Is the voice on (🔊)? Is it paused (⏸)? **System check** shows whether the chat voice source is running in OBS. |
| **Voice sounds robotic / "offline Windows voice"** | The online voice wasn't reachable. IXC uses the offline voice for a minute and then tries again by itself. |
| **A platform says "Reconnecting…"** | IXC retries by itself, faster when your internet comes back. **Platforms & accounts** shows the reason. Kick sometimes blocks lookups for a while; once IXC has found your chat room it keeps working. |
| **YouTube says "not live right now"** | IXC finds your live stream by itself every 30 seconds. Check the channel (`@yourhandle`), or paste the link of the live video. |
| **"Sign-in expired"** | **Platforms & accounts > Sign in again.** |
| **Can't reply on a platform** | Connect that account (**Platforms & accounts**). Rumble doesn't allow replies from apps. |
| **Phone shows "Waiting for your PC"** | IXC on the PC is closed, asleep or offline. The phone reconnects by itself when it's back. |
| **Phone says "This phone was signed out"** | It was removed on the PC, or not used for a long time (Settings: "Forget phones not used for"). Scan a new QR code. |
| **QR code "expired or already used"** | Codes work once, for 5 minutes. Press **New code**. |
| **IXC moved to another port** | Another program uses port 8767. IXC picks the next free port and updates its OBS sources by itself. Close OBS and use **System check > Add IXC panels** to update the panels. |
| **Settings look reset** | If the settings file got damaged, IXC restores the last good copy and tells you. **System check > Backups** lets you restore any backup. |
| **An overlay is empty** | It only shows something when there's something to show (for example, music playing). Use **Overlays** to preview it with example data. |
| **Update failed** | Nothing was changed: IXC checks downloads before installing, and puts the old version back if the new one doesn't start. Try again later, or download `IXC-Setup.exe` again. |

**Logs:** `%LOCALAPPDATA%\IXC-OBS\logs\`, one file per area (app, obs, chat, tts, music, phone, auth…). You can also open them from **System check > Open logs folder**.
