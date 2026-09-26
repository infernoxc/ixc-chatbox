# Changelog
All notable changes are listed here ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/), [SemVer](https://semver.org)).

## [Unreleased]

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

[Unreleased]: https://github.com/infernoxc/ixc-chatbox/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/infernoxc/ixc-chatbox/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/infernoxc/ixc-chatbox/releases/tag/v1.0.0
