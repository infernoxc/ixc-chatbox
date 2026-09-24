# Third-party notices

The code in this repository is written by Ishan (InFerNoxC) and released under the [MIT License](LICENSE). IXC ChatBox **talks to
or loads at runtime** the components below. None of their code or assets are included here, they aren't covered by this project's
license, and this project claims no ownership of them.

| Component | How it is used | Owner / terms |
|---|---|---|
| **Streamer.bot** | Its WebSocket server delivers chat events and sends replies. | Streamer.bot, free, proprietary ([streamer.bot](https://streamer.bot)) |
| **OBS Studio** | The pages run in OBS browser sources and docks. No OBS code is included or linked. | OBS Project, GPL-2.0 |
| **Twitch, Kick, YouTube** | Chat content, plus emote, badge and avatar images shown from their CDNs. | Twitch Interactive, Kick Streaming, Google, and their users |
| **Microsoft Edge "Read aloud" speech endpoint** (`speech.platform.bing.com`) | Neural TTS voices via `helper/edge-tts.ps1`. Unofficial and undocumented; may stop working. The client token is a public constant shipped in Microsoft Edge (also used by the open-source [edge-tts](https://github.com/rany2/edge-tts) project). | Microsoft, [Microsoft Services Agreement](https://www.microsoft.com/servicesagreement) |
| **qrcodejs 1.0.0** (cdnjs) | Loaded only when you open the phone dialog, to draw the QR code. | davidshimjs, MIT |
| Fonts: Bahnschrift, Segoe UI, Consolas | Referenced by name; they come with Windows. Not distributed. | Microsoft |
| Windows PowerShell 5.1 / .NET Framework 4.8 (`HttpListener`, `System.Web.Extensions`, C# compiler) | Runtime. | Microsoft |

The banner in `docs/images` is original artwork for this project (MIT). The dock screenshot shows this project's UI in demo mode
with made-up usernames; the emote images in it belong to their owners.
Trademarks belong to their owners. Not affiliated with or endorsed by Streamer.bot, the OBS Project, Twitch, Kick, Google/YouTube or Microsoft.