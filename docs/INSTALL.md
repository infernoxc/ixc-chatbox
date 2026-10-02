# Install IXC

## The normal way

1. Install **OBS Studio** if you don't have it yet ([obsproject.com](https://obsproject.com)), and **open it once**.
2. Download **`IXC-Setup.exe`** from [Releases](https://github.com/infernoxc/ixc-chatbox/releases/latest) and run it.
   - **"Windows protected your PC"**: click **More info**, then **Run anyway**. This appears because IXC is a free project without a paid code-signing certificate. The download's checksum is listed in `SHA256SUMS.txt` next to it.
3. Choose **Express (recommended)**.
   - If OBS is open, the setup asks you to close it (**File > Exit**). OBS saves its settings when it closes, so IXC can only add its panels while it's closed.
4. Click **Finish**. IXC opens a setup assistant:

| Step | What happens |
|---|---|
| **OBS** | IXC turns on OBS's WebSocket server and adds a scene called **IXC Audio** (music player + chat voice) to all your scenes. It's invisible on stream; it only carries the sound. |
| **Platforms** | Click **Connect** for Twitch, Kick and YouTube, and sign in in your browser. Or just type your channel name: reading chat needs no sign-in. |
| **Sound check** | Play a test voice. Turn the chat voice and music ducking on or off. |
| **Phone** *(optional)* | Scan the QR code with your phone's camera. |
| **Overlays** | Add Now Playing, Chat, Viewers or Alerts to the scene that's on air. |
| **System test** | Checks everything. Anything wrong has a **Fix** button. |

Done. IXC starts with Windows and lives in the tray (bottom right). **Double-click the tray icon** to open IXC.
In OBS you'll find the **IXC Chat**, **IXC Music** and **IXC** panels under **Docks**.

## Advanced setup
Choose **Advanced** in the setup to decide:
- whether IXC starts with Windows,
- whether it changes OBS's settings,
- whether it adds a desktop icon.

## Without the setup program (ZIP)
Unzip `IXC-Suite-vX.Y.Z.zip` and double-click **`Install.bat`**. It does the same as the setup program.

## From source
```powershell
git clone https://github.com/infernoxc/ixc-chatbox.git
cd ixc-chatbox
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```
This builds IXC with the C# compiler that ships with Windows. Nothing is downloaded.

## What the setup changes on your PC
| Where | What |
|---|---|
| `%LOCALAPPDATA%\IXC-OBS\` | The program (`app\`), your settings, encrypted sign-ins, the music queue, logs, backups |
| Task Scheduler | "IXC for OBS": starts IXC when you log in (only for you) |
| Start menu | IXC, Uninstall IXC |
| OBS | Turns on the WebSocket server; adds the panels and the scene "IXC Audio" (a backup of OBS's settings file is made first) |

Nothing needs administrator rights, and no firewall rules or open ports are created.

## Updating
IXC tells you when a new version is out (**Home** or **System check > Updates**). Click **Update now**. IXC then:
1. backs up your settings,
2. downloads the update and checks its fingerprint,
3. installs it and restarts.

If the new version doesn't start, the previous one is put back automatically.
