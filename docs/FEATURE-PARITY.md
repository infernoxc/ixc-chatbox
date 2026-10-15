# IXC feature parity: standalone, no Streamer.bot

Audit of IXC 3.0.4 (`main` at e39cb7f, 2026-10-15) against the goal: one installer, one app, no Streamer.bot, updated with what 3.1.0 adds.
Status is what the code and tests show, not what the docs or UI say.

**Test labels**
- **auto (mock)**: automated test against fake platform, OBS or GitHub servers. It proves IXC's side, not the real service.
- **auto (Windows)**: CI on a clean Windows machine, with the real installer and the Windows compiler.
- **live (CI)**: CI against the real public platform, without an account.
- **manual**: needs a person with real accounts. This hasn't been done unless it says so.

## 1. What Streamer.bot does for IXC today

Streamer.bot is optional: `platforms.streamerbot.mode` is `auto`, which means IXC uses it only when it's running. Nothing in IXC
needs it to start or to read chat. A streamer who runs it, as the author does, gets four things from it:

| # | Streamer.bot provides | Why IXC uses it | Replacement |
|---|---|---|---|
| S1 | **Sending replies** to Twitch, Kick and YouTube | **Connect account** needs app IDs, and YouTube/Kick also need a relay. Neither has been set up for the published build. | ✅ 3.1.0 **Your apps**: you enter your own app IDs in IXC, with secrets stored encrypted on your PC. No relay, and nothing secret in the installer. |
| S2 | **Timed messages** (e.g. "Follow InfernoXC everywhere") | IXC had no timers. | ✅ 3.1.0 native Timers. |
| S3 | **Custom commands** (`!drop`, `!discord`, `!socials`, …) | They live in Streamer.bot. IXC only reads their names, to show in the `!` list. | ✅ 3.1.0: import the names from Streamer.bot, then you fill in the replies once. |
| S4 | **Event reactions** (thank-you on follow, sub or raid) | IXC showed alerts but never posted a chat message for them. | ✅ 3.1.0 event messages. |
| S5 | YouTube stream ID and viewer number when YouTube's page hides the stream | Fallback only. | 3.0.3 already checks the live chat directly, so it no longer needs Streamer.bot. |

## 2. Feature checklist

| Area | Feature | Implementation | Status | Tests |
|---|---|---|---|---|
| **OBS** | Find OBS, read its WebSocket port and password from OBS's own settings | `Obs.cs` (`WsConfig`, `Detect`) | ✅ | auto (mock) `05-obs`; auto (Windows) smoke |
| | Turn on OBS's WebSocket server (OBS closed), with a backup of its file | `Obs.EnableWebSocket` | ✅ | auto (mock) |
| | Manual address and password, Test connection | OBS page, `ObsControl.SetConnection` | ✅ 3.1.0 | auto (mock) `14` |
| | Reconnect after OBS restarts | `WsLink` backoff, max 15 s | ✅ | auto (mock) |
| | Add docks, the IXC Audio scene and its sources | `Obs.AutoSetup`, `AddDocks` | ✅ | auto (mock); manual on real OBS: **not done** |
| | Per-platform audio tracks for music | `SetInputAudioTracks` | ✅ | auto (mock) |
| | **Streaming and recording state** | Outputs events, `ObsControl` | ✅ 3.1.0 | auto (mock) `14` |
| | **Scene switching, show/hide a source** | OBS page, API, command actions | ✅ 3.1.0 | auto (mock + browser) `14`, `ui` |
| **Chat in** | Twitch: IRC, anonymous or signed in | `TwitchIrc` | ✅ | auto (mock); live (CI) connects |
| | Kick: Pusher WebSocket with keepalive | `KickLink` | ✅ | auto (mock); live (CI) connects |
| | YouTube: the website's live-chat endpoints; finds the stream | `YouTubeSource` | ✅ (3.0.4) | auto (mock); live (CI) only offline, **not while live** |
| | Rumble: personal Live Stream API link | `RumbleSource` | ✅ | auto (mock) |
| | Streamer.bot as an extra source | `SbLink` | ✅ optional | auto (mock) |
| **Chat out** | Twitch reply | IRC with your sign-in | ✅ (needs Your apps) | auto (mock) `07` |
| | Kick reply | public API `POST /chat` | ✅ (needs Your apps) | auto (mock) `11`; manual: **not done** |
| | YouTube reply | Data API `liveChatMessages.insert` | ✅ (needs Your apps) | sign-in auto (mock) `11`; sending manual: **not done** |
| | Rumble reply | Rumble has no API for this | ⛔ not possible | — |
| **Accounts** | Twitch device sign-in, refresh, revoke | `Accounts.cs` | ✅ | auto (mock) |
| | YouTube and Kick PKCE sign-in with `state` check | `Accounts.cs` (own secret, relay or build secret) | ✅ | auto (mock) `11` |
| | **Your own app IDs entered in IXC** | `Accounts.SetApps`, Your apps card | ✅ 3.1.0 | auto (mock) `11` |
| | Tokens encrypted (DPAPI), never in logs or config | `Secrets` | ✅ | auto (mock) |
| **Moderation** | Delete message / timeout / ban from the panel | `Moderation.cs`: Twitch Helix, YouTube Data API, Kick public API (timeout/ban only) | ✅ 3.1.0 | auto (mock) `15`; manual: **not done** (Kick request fields not verified against Kick's docs from here) |
| **Commands** | Built-ins `!np !queue !skip !uptime !wrongsong !ttsskip !ttson !ttsoff !pause !resume !viewers !songs` | `Commands.cs` | ✅ | auto (mock) `03` |
| | Custom text commands: permission, cooldowns, aliases, platforms, variables | `Commands.cs`, editor in app | ✅ | auto (mock) |
| | Different reply per platform | `responses` per command | ✅ 3.1.0 | auto (mock) `13` |
| | OBS actions from a command (`!brb` → scene) | `obs` per command | ✅ 3.1.0 | auto (mock) `14` |
| | Import from Streamer.bot | names; you type the answers | ✅ 3.1.0 | auto (mock) `13` |
| **Timers** | Repeating messages | `Timers.cs`, Timers page | ✅ 3.1.0 | auto (mock + browser) `12`, `ui` |
| **Events** | Alerts: follow (Twitch signed in, Kick, Rumble), sub, gift, raid, superchat, member | `Chat.Ingest` kinds | ✅ shown and on overlay | auto (mock) |
| | Chat message on an event | `Events.cs` | ✅ 3.1.0 | auto (mock) `13` |
| **TTS** | Neural voices, offline Windows fallback | `Tts.cs` | ✅ | auto (mock, fake TTS) |
| | Per-user, per-role, per-platform voices, filters, queue, rate limits, stale skip | `Tts.cs` | ✅ | auto (mock) |
| | Audio output | plays in the OBS browser source **IXC TTS**; device and monitoring are chosen in OBS | ✅ (by design) | manual: **not done** |
| **Music** | YouTube, Spotify links, local folders, queue that survives restarts, autoplay | `Music.cs`, `player.html` | ✅ | auto (mock) `04` |
| | Song requests: permissions, cooldowns, limits, bans, `!wrongsong` | `SongRequests.cs` | ✅ | auto (mock) |
| | Ducking while TTS speaks, always restores | `Music.cs`, `Tts.cs` | ✅ | auto (mock) |
| | Per-platform routing | OBS audio tracks on the music source (needs one output per platform in OBS's multistream setup) | ✅ | auto (mock); manual: **not done** |
| **Viewers** | Twitch (Helix signed in, public data otherwise), Kick, YouTube, Rumble; stale numbers hidden | `Viewers.cs` | ✅ | auto (mock); live (CI) |
| **Overlays** | Now playing, queue, chat, viewers, TTS, alerts, status; editor; Add to OBS | `src/overlay`, `Links.cs` | ✅ | auto (browser) |
| **Phone** | Quick connect (Cloudflare quick tunnel), one-time QR codes, device keys, revoke, allow-list | `Quick.cs`, `Remote.cs` | ✅ | auto (mock + Chromium) |
| | Permanent relay | `relay/` | ✅ optional | auto (relay in Cloudflare's local runtime) |
| **System** | System check and "Fix automatically", backups, diagnostics without secrets | `Health.cs` | ✅ | auto (mock) |
| | Updates: verified download, rollback; install by themselves, not while live | `Health.cs` `Updates` | ✅ | auto (mock); auto (Windows) update test |
| **Installer** | One `IXC-Setup.exe`, Express/Advanced, backup, keeps settings, rollback if IXC won't start, clean uninstall keeping data | `build/installer.iss`, `scripts/install.ps1` | ✅ | auto (Windows) clean machine, upgrade, uninstall |

## 3. Plan (dependency order) - done in 3.1.0

1. **Your own apps**: Twitch Client ID; Google client ID + secret (Desktop app); Kick client ID + secret, entered in IXC. Secrets go
   into the encrypted store, and IXC talks to Google/Kick directly (no relay). Removes S1.
2. **Timers**: message, interval, minimum chat lines in between, platforms, live only, on/pause. One scheduler, no repeats after
   reconnects or restarts. Removes S2.
3. **Event messages, per-platform replies, Streamer.bot import**: removes S3 and S4.
4. **OBS control**: streaming/recording state, scenes, show/hide sources, command actions, Test connection.
5. **Moderation**: delete / timeout / ban where the platform's official API allows it.
6. Streamer.bot labelled "optional bridge (not needed)". Release 3.1.0.

## 4. Known platform limits (not fixable in IXC)
- **Rumble**: no API to send chat or moderate. IXC reads chat only.
- **YouTube reading**: IXC reads chat with the endpoints youtube.com uses, which are unofficial and can change. The official API costs
  quota for reading, so it's used only for sending.
- **Kick reading**: same (Pusher endpoint used by kick.com). Sending uses Kick's official public API.
- **App credentials**: Twitch, Google and Kick require a developer app. Only the account owner can create one. IXC can't create it,
  and a secret must never ship inside a public installer.
- **Per-platform audio**: Windows has no per-platform audio. IXC uses OBS audio tracks, and each track goes to an OBS output
  (multistream plugin or separate outputs) set up in OBS.

## 5. Still needs a person with real accounts (not done)
1. Your apps + Connect on real Twitch, YouTube and Kick; a reply, a timer and an event message on each.
2. Moderation on each (delete / timeout / ban), especially Kick, whose request fields couldn't be checked against Kick's docs here.
3. A real YouTube stream from a home connection (detection, staying connected, ending).
4. Real OBS 30+: scene switching, show / hide, streaming state, `!brb`.

## 6. 3.2.0 audit (all-in-one task), 2026-10-15

**Streamer.bot**: not needed for anything. No code path requires it; `platforms.streamerbot.mode` = `auto` only uses it when it is
already running, and **Never** turns it off completely. Every feature in section 2 works with it off (CI runs the suite with it off).

| Area | Feature | Status | Tests |
|---|---|---|---|
| Audio | Music ON/OFF per platform (independent switches) | ✅ 3.2.0 `Routing.cs`, Audio routing page, dock | auto (mock OBS, browser) `16`, `ui` |
| | Chat voice (TTS) ON/OFF per platform | ✅ 3.2.0 same mechanism on the "IXC TTS" source | auto (mock OBS) `16` |
| | Real effect on viewers | needs one OBS output per platform, each on its own track | manual: **not done** |
| OBS | Start IXC when OBS opens, stop after OBS closes, one copy only | ✅ 3.2.0 `Supervisor.cs` (tray watches `obs64.exe`) | auto (Windows) `smoke.ps1` 2b with a stand-in `obs64.exe` |
| Music | Shuffle without repeats, never twice in a row | ✅ 3.2.0 (was: random pick each time → repeats) | auto (mock) `17` |
| | Autoplay skips songs that played recently (also other uploads) | ✅ 3.2.0 | auto (mock) `17` |
| | Playlists, history | ✅ 3.2.0 | auto (mock + browser) `17`, `ui` |
| Commands | `!commands` `!followage` `!clip` `!discord` `!socials` presets | ✅ 3.2.0 | auto (mock Helix + OBS) `18`, `ui` |
| Accounts | Last message time, reply ability, expired sign-in shown | ✅ 3.2.0 | auto (browser) |
| Performance | idle / busy CPU and RAM | measured on the CI Windows runner (see the CI log, `PERF` lines) | auto (Windows) |

### Platforms that were asked for but are not added (and why)
- **TikTok LIVE**: TikTok has no official API for reading or sending LIVE chat. The libraries that exist reverse-engineer TikTok's
  website; that is the kind of unauthorized scraping this project doesn't do. Not added.
- **Facebook Live**: official Graph API exists (`/{live-video-id}/comments`, Server-Sent Events). It needs a Meta app that passes Meta's
  App Review for Page permissions, and a Page; nothing IXC can do for you. Possible later; not added because it can't be tested here.
- **Instagram Live**: comments only through a webhook (`live_comments`) for Business accounts, which needs a public HTTPS server and
  App Review. A desktop app can't receive it without a relay. Not added.
- **Multiple accounts per platform**: one account per platform (the channel you stream on). Not added.

### OBS plugins
- **Aitum Multistream**: IXC doesn't control it (no documented obs-websocket requests). Per-platform audio works if each Aitum output can
  be set to its own audio track; that could not be checked here. **jrDockie** (saves/loads dock layouts): IXC's docks are ordinary OBS
  browser docks, so jrDockie saves them like any other dock; nothing to do in IXC.

### Not tested (needs your PC / accounts)
Real OBS with real outputs (audio routing heard on each platform), real Twitch clip / follow age, start-with-OBS on your PC
(tested on a clean CI Windows with a stand-in process, not with real OBS), TTS heard through your speakers/OBS.
