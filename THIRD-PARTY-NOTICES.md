# Third-party notices

The code in this repository is written by Ishan (InFerNoxC) and released under the [MIT License](LICENSE), except the bundled
QR code generator listed below. IXC **talks to or loads at runtime** the other components below. Their code isn't included here, it
isn't covered by this project's license, and this project claims no ownership of it.

| Component | How it is used | Owner / terms |
|---|---|---|
| **QR Code Generator for JavaScript** (`src/core/web/qrcode.js`, bundled) | Draws the phone pairing QR code locally. | © 2009 Kazuhiko Arase, MIT License (notice kept in the file) |
| **OBS Studio** | Docks, browser sources and obs-websocket 5 (setup and audio routing). No OBS code is included or linked. | OBS Project, GPL-2.0 |
| **Twitch** | Chat over IRC/WebSocket, EventSub, Helix API, emote images, and twitch.tv's public data for viewer numbers without sign-in (unofficial). | Twitch Interactive, [Developer Agreement](https://legal.twitch.com/legal/developer-agreement/) |
| **Kick** | Chat over its public chat socket (Pusher), its channel data and the official public API. | Kick Streaming |
| **YouTube** | Live chat as used by youtube.com, YouTube Data API v3 (replies, optional search), oEmbed, the IFrame player, thumbnails. | Google, [YouTube API Services Terms](https://developers.google.com/youtube/terms/api-services-terms-of-service) |
| **Rumble** | Your personal Live Stream API link. | Rumble |
| **Spotify** | oEmbed and Web API to read song names (audio is never streamed). | Spotify, [Developer Terms](https://developer.spotify.com/terms) |
| **Streamer.bot** (optional) | Its WebSocket server, when you use it. | Streamer.bot, proprietary |
| **Microsoft Edge "Read aloud" speech endpoint** | Neural chat voices. Unofficial and undocumented; may change or stop working. | Microsoft, [Services Agreement](https://www.microsoft.com/servicesagreement) |
| **Windows speech voices (SAPI)** | Offline backup voice. | Microsoft |
| **Cloudflare `cloudflared` (quick tunnels)** | Quick connect for the phone remote. Downloaded once from Cloudflare's GitHub releases, signature checked, not shipped with IXC. | Cloudflare, Apache-2.0; [Cloudflare terms](https://www.cloudflare.com/website-terms/) |
| **Cloudflare Workers / Durable Objects** | Hosts the IXC relay (`relay/`), deployed by the publisher of IXC. | Cloudflare, [terms](https://www.cloudflare.com/website-terms/) |
| **Google Fonts** | Only if you choose a non-Windows font for an overlay. | Google / font authors, OFL |
| .NET Framework 4.8, Windows PowerShell 5.1, Microsoft Edge (app window) | Runtime. | Microsoft |
| Development only: Mono, Node.js, `ws`, `wrangler`, Playwright, Inno Setup | Building and testing; not shipped. | Their authors (MIT / Apache-2.0 / Inno Setup license) |

Trademarks belong to their owners. Not affiliated with or endorsed by the OBS Project, Twitch, Kick, Google/YouTube, Rumble, Spotify,
Streamer.bot, Microsoft or Cloudflare.
