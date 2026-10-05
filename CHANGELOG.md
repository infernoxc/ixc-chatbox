# Changelog
All notable changes are listed here ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/), [SemVer](https://semver.org)).

## [Unreleased]

## [3.0.2] - 2026-10-05
### Fixed
- YouTube chat: a short "ended" answer from YouTube no longer flips the chip to Not live; IXC re-checks the stream up to 3 times first.
- YouTube chat: messages sent while IXC was reconnecting (same stream, gap up to 5 minutes) are now read out and treated as new instead of being marked old and skipped. Repeats are still dropped by message id.
- YouTube chat: after losing a live stream IXC looks twice more (4 s apart) before showing Not live, and the Not live chip now says why (for example "scheduled but not started - press Go live in YouTube Studio").
- Kick chat: IXC now sends a keepalive ping every 45 s. A quiet chat no longer looked dead and was no longer dropped and reconnected every ~150 s.
- TTS: the "busy chat" limits can no longer be dragged down to values that silently drop most messages (waiting messages 3-50, per minute 5-120, skip-older-than 20 s and up). New defaults read more of a busy multi-platform chat: 15 waiting, skip after 90 s, 40 per minute, same person waits 2 s.


## [3.0.1] - 2026-10-02
### Added
- Phone remote with **Quick connect**: Show QR code works with no setup (Cloudflare's free quick tunnel, temporary link, signature-checked download, only the phone page reachable). The relay stays optional, for phones that stay paired across restarts.
- Replies go through Streamer.bot for platforms IXC reads but isn't signed in to.
- The "!" list shows every IXC and Streamer.bot command, labelled, in the chat panel and on the phone.
- The chat panel and the phone show viewers watching and people chatting. Twitch viewer numbers work without signing in.
### Fixed
- Typing a channel name on the platforms page or in the setup assistant was wiped by live updates.
- "No chat is connected" although Streamer.bot was connected; Twitch viewers showed 0 while live.
- A removed phone sometimes only saw "disconnected" instead of "signed out".
- Release: a run can be started again from the Actions page for an existing release.

## [3.0.0] - 2026-10-02
IXC Music and IXC ChatBox are now one suite with one installer (`IXC-Setup.exe`), and **Streamer.bot is no longer needed**.
### Added
- Built-in chat connections: Twitch (no sign-in needed to read; sign-in for replies, follower alerts, viewers), Kick, YouTube (finds your live stream by itself), Rumble (Live Stream API). Each platform has its own status: Connected / Reconnecting / Sign-in required / Rate limited / Not live / Off. Streamer.bot stays supported as an option.
- "Connect account" for Twitch (device code), YouTube and Kick (through the IXC relay); tokens encrypted and refreshed automatically.
- Dashboard window (tray icon) with a first-run wizard, System check with "Fix automatically", repairs, backups/restore, diagnostics export and safe updates (backup, checksum, rollback).
- Song requests (`!sr`, `!song`) with permissions, cooldowns, limits, banned songs/artists, maximum length, `!wrongsong`.
- Command manager: built-in `!np !queue !skip !uptime !viewers !wrongsong !ttsskip !ttson !ttsoff` + your own commands.
- Chat filters (links, caps, repeats, length, duplicates, copy-paste spam, symbols, banned words, block/allow lists, minimum role on stream) and platform filters for the panel, overlays and the voice.
- Chat voice: per-person, per-role and per-platform voices (voice, speed, pitch, volume, on/off), pause/resume, moderators can skip, 4 more voices.
- Music ducking: smooth fade down/up while the voice speaks; the normal volume is never changed and always comes back.
- Local music folders (MP3, M4A, AAC, OGG, OPUS, WAV, FLAC) next to YouTube and Spotify links.
- Viewer counter per platform and total (stale numbers are never shown), Now Playing JSON/text outputs.
- Overlays: Now Playing (full/compact/minimal), Up Next, Chat, Viewers, Alerts, chat-voice indicator, Live status badge, styled live from the dashboard and added to OBS with one click.
- OBS auto-setup: WebSocket server, panels, and the "IXC Audio" scene with the music player and voice in every scene.
- Phone remote through a stable relay: pairing survives restarts and network changes; devices can be renamed/removed; latency shown.
- Tray supervisor: IXC restarts itself after a crash (with a crash-loop guard).
### Changed
- The music queue lives in IXC (`music.json`), so OBS restarts, cache clears or source reloads never lose it. The v2 queue is taken over automatically.
- IXC uses its own small web server on the loopback address instead of Windows' http.sys (no URL reservations, no access-denied errors); a busy port is handled automatically.
- Release builds ship a ready-made `ixc-core.exe` and start it directly (no PowerShell window at login).
- Settings are validated, saved at once, and a damaged settings file is replaced by the last good copy.
### Fixed
- Any website could control IXC through a sandboxed frame (`Origin: null`): such requests are now refused, as are foreign `Host` headers.
- The chat voice read numbers wrong ("10000" was read as "100").
- A temporary internet problem made a YouTube link fail for 10 minutes (failures were cached).
- One blocked video skipped a whole YouTube playlist.
- Failed connection attempts to OBS/Streamer.bot were never closed (a socket leak on every retry).
- A wrong Streamer.bot password still showed "connected".
- Installing an older app could replace a newer shared core (the version check could never work).
- Restoring a backup could be undone by IXC saving its in-memory settings on exit.
- Setting an API key wrote it into the log.
- Song requests with a playlist link now play just that video.
- Local songs are still found when YouTube search is unreachable.
### Removed
- The cloudflared quick tunnel (replaced by the relay), the separate diagnostics and phone pages (now in the dashboard and the relay), `config.example.json` (IXC creates its settings).

## [2.0.1] - 2026-09-26
### Fixed
- Switching the shared music destination (ALL / TWITCH ONLY / KICK ONLY / YOUTUBE ONLY) right after OBS starts could fail with
  "OBS is not ready to perform the request." It now retries automatically until OBS finishes loading, instead of failing once.

## [2.0.0] - 2026-09-24
### Changed
- **IXC Core**: one small native program replaces the PowerShell helper and the chat relay. In a 60-second test with the usual OBS pages
  open, IXC used 0.011 % CPU and 42 MB RAM, against 0.065 % and 152 MB for v1. Pages get updates pushed over one WebSocket instead of polling
  (about 260 requests a minute less), and one shared Streamer.bot connection serves every page.
- **TTS now runs inside IXC**, not inside the dock page. It reads chat whenever IXC is running, and its settings are the same in every dock and on the phone.
  (Fixes v1 reading only while the dock page was open with its own per-page ON switch.)
- Chat pages show recent history as soon as they open, and remove deleted messages and messages from banned users.
### Added
- TTS: *ignore my account* (found automatically from Streamer.bot), *ignore bots*, a never-speak list, blocked words, and read-emotes / emoji / links switches.
- TTS text cleanup: links, emotes, emoji, repeated letters and words, long numbers, shouting, `@user_name123`.
- TTS for busy chat:
  - a message mirrored on several platforms is read once, and duplicate events are dropped;
  - queue size, "too old" drop, per-user and per-minute limits, and `!tts` priority;
  - speed, pitch and volume;
  - queue, history with replay, skip and clear;
  - a *not read (and why)* list.
- Two new, slightly faster Indian voices: **Neerja Expressive** (female) and **Madhur** (male), and an **offline Windows voice** fallback that switches in automatically.
- **Phone remote over the internet** (mobile data too):
  - Cloudflare quick tunnel over HTTPS/WSS, with no router or firewall changes;
  - one-time QR pairing codes and expiring sessions, with origin and host checks and rate limits;
  - a mobile page with chat, TTS and music controls and status, which reconnects by itself.
- **Diagnostics page** (`/diag`): connections, TTS engine and queue, phone tunnel, CPU and RAM, recent events.
- Smoke tests for WebSocket push, TTS filters and phone-access security.
### Removed
- The v1 Wi-Fi-only phone view, `enable-phone-access.ps1`, and the separate relay port 8768.

## [1.0.0] - 2026-09-24
### Added
- Twitch + Kick + YouTube chat via Streamer.bot, in dock and overlay modes; emotes, badges, avatars, platform tags, viewer counts.
- Reply box (All / Twitch / Kick / YouTube), `!command` and `@user` suggestions, click-to-reply (chat relay with an authenticated Streamer.bot connection).
- Neural text-to-speech (5 configurable voices) through an OBS browser source, with a Windows voice fallback and bot/command/link filtering.
- Optional phone view (private networks only, random key, QR code).
- Per-user installer and uninstaller (no admin), start at login, `-AddObsDocks`, `config.json`, smoke tests, CI, a portable ZIP and an Inno Setup installer.

[Unreleased]: https://github.com/infernoxc/ixc-chatbox/compare/v3.0.1...HEAD
[2.0.0]: https://github.com/infernoxc/ixc-chatbox/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/infernoxc/ixc-chatbox/releases/tag/v1.0.0
