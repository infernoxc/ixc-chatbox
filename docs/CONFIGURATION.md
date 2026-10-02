# All settings

Every setting is in the IXC window. **You never need to edit files.** This page lists what each setting does, for reference.
IXC stores them in `%LOCALAPPDATA%\IXC-OBS\config.json`. It checks every change, and if that file ever gets damaged, it restores the last
good copy by itself. Sign-ins, keys and phone keys are **not** in this file: they are encrypted in `secrets.dat`.

| Area | Settings (IXC window) |
|---|---|
| **Platforms & accounts** | Use Twitch/Kick/YouTube/Rumble, channel, Connect account, Streamer.bot (auto / always / never) |
| **Chat filters** | Links, caps (%), repeated characters, very long messages, same person repeating (within N s), copy-paste spam, weird characters, banned words (`*` wildcard), always hide / never filter people, moderators and subscribers skip filters, "show on stream" minimum role. Each filter can be: **Off**, **Don't read it out**, or **Hide on stream** |
| **Commands** | Built-in and own commands: name, other names, answer (variables), who, cooldowns, platforms |
| **Song requests** | On/off, commands (`!sr`, `!song`), who, platforms, answer in chat, max waiting / per person, cooldowns, longest song, minimum account age (Twitch), links / song names allowed, banned songs and artists |
| **Chat voice** | On, paused, default voice, read mode, speed, pitch, volume, offline backup voice, platforms, who, skip own messages / bots, read emotes / emoji / links, longest message, moderator control, busy-chat limits, never-read / blocked-word / bot / own-name lists |
| **Voices for people** | Voice, speed, pitch, volume, on/off per person, per role (you, moderators, VIPs, subscribers) and per platform |
| **Music** | Ducking (on, level, fade down/up, minimum), autoplay, repeat, shuffle, start with OBS, music folders, which OBS track each platform uses, Now Playing text format and file |
| **Overlays** | Per overlay: layout, contents, font, size, colors, background opacity, corners, alignment, animation |
| **Phone remote** | On/off, forget phones unused for N days, paired phones |
| **Settings** | Restart IXC automatically after a crash, check for updates, open the window with Windows, viewer refresh interval, Advanced mode |
| **Advanced mode** | Detailed logs, IXC port, OBS WebSocket address, phone relay address, Streamer.bot address and settings file, extra commands file, hidden command suggestions, YouTube Data API key, Spotify app keys, OBS music source name |

Power users can read and change everything with the local API (`GET/POST /api/settings`), which uses the same validation.
The setting names are shown in the API response together with their type, default and limits.
