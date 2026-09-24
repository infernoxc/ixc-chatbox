# Third-party notices

The code in this repository is written by Ishan (InFerNoxC) and released under the [MIT License](LICENSE). IXC ChatBox **talks to,
downloads or loads at runtime** the components below. None of their code or assets are included here, they aren't covered by this
project's license, and this project claims no ownership of them.

| Component | How it is used | Owner / terms |
|---|---|---|
| **Streamer.bot** | Its WebSocket server delivers chat events and sends replies. | Streamer.bot, free, proprietary ([streamer.bot](https://streamer.bot)) |
| **OBS Studio** | The pages run in OBS browser sources and docks; IXC talks to OBS's built-in obs-websocket. No OBS code is included or linked. | OBS Project, GPL-2.0 |
| **Twitch, Kick, YouTube** | Chat content, plus emote, badge and avatar images shown from their CDNs. | Twitch Interactive, Kick Streaming, Google, and their users |
| **Microsoft Edge "Read aloud" speech endpoint** (`speech.platform.bing.com`) | Neural TTS voices (`src/core/Tts.cs`). Unofficial and undocumented; may change or stop working. The client token is a public constant shipped in Microsoft Edge (also used by the open-source [edge-tts](https://github.com/rany2/edge-tts) project). | Microsoft, [Microsoft Services Agreement](https://www.microsoft.com/servicesagreement) |
| **Windows speech voices (SAPI)** | Offline fallback voice, from the voices installed in Windows. | Microsoft |
| **cloudflared** | Downloaded once, on first use of the phone remote, from [Cloudflare's GitHub releases](https://github.com/cloudflare/cloudflared/releases), and kept only with a valid Cloudflare signature. Runs a free "quick tunnel" (`trycloudflare.com`), which Cloudflare provides for testing, without uptime guarantees. | Cloudflare, Apache-2.0; tunnel service under [Cloudflare's terms](https://www.cloudflare.com/website-terms/) |
| **qrcodejs 1.0.0** (cdnjs) | Loaded only when you open the QR window, to draw the QR code. | davidshimjs, MIT |
| Fonts: Bahnschrift, Segoe UI, Consolas | Referenced by name; they come with Windows. Not distributed. | Microsoft |
| .NET Framework 4.8 (`HttpListener`, `System.Web.Extensions`, `System.Speech`, C# compiler) and Windows PowerShell 5.1 | Runtime and build. | Microsoft |

The banner in `docs/images` is original artwork for this project (MIT). The dock screenshot shows this project's UI in demo mode
with made-up usernames; the emote images in it belong to their owners.
Trademarks belong to their owners. Not affiliated with or endorsed by Streamer.bot, the OBS Project, Twitch, Kick, Google/YouTube, Microsoft or Cloudflare.
