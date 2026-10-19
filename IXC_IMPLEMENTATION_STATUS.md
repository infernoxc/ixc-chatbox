# IXC Implementation Status

## Goal
Repair platform connections, preserve existing IXC features, stabilize OBS/Aitum workflows,
reduce resource overhead, and prepare a verified Windows release candidate.

## Baseline (2026-10-19, branch claude/ixc-stability-20261019, HEAD bbe0a6a)
- Core compiles on Windows with `src/core/build-core.ps1` (csc, .NET Framework) - OK.
- No Mono / WSL / python3 on this PC. `tests/integration/harness.mjs` now accepts `IXC_CORE_EXE=<path to ixc-core.exe>` to run the existing
  mock-server tests against the Windows build (default Mono path unchanged). `ws` npm package installed locally in tests/ (not committed).

## Confirmed root causes
- No product defect in Twitch / YouTube / Kick connection code reproduced against the mock servers (see Tests). The Kick key/cluster fix
  (3.2.0) and YouTube bot-check API fallback are covered and pass here.
- Test race found (fixed): 02-chat "Kick + YouTube + Rumble" sent a Kick message before the core had handled the test page's hello,
  so the page never got it (message was in history only). Failed 3/3 on Windows before the fix; passes after.

## YouTube diagnosis (2026-10-19)
- How it works: chat is READ from YouTube's own website live-chat (InnerTube; the "API key" in the code is scraped from the page, no Google key or sign-in needed).
  Google OAuth (the streamer's own Google app: client ID + secret, scope youtube.force-ssl) is only used to SEND, moderate, and as a fallback to find the
  live broadcast via liveBroadcasts.list when the page shows the bot check. Streamer.bot is optional and not needed.
- Checked on this PC: channel @infernoxc00 (saved in %LOCALAPPDATA%IXC-OBS) exists, page loads without a bot check, was not live -> IXC correctly says "not live".
  IXC-OBS logs (last 2026-10-17) show no Google account connected and no YouTube error. A live-stream failure could not be reproduced here.
- Confirmed cause of "YouTube sign-in expires": the in-app guide told users to leave the Google app in Testing; Google ends refresh tokens after 7 days
  (400 invalid_grant), IXC marked the account invalid with a generic message. Fixed: message + guide now say "Publish app".
- Still unverified: a real Google OAuth sign-in and real live chat (needs the user's Google app + a live stream).

## Changes completed
- Accounts.cs: YouTube refresh refusal reason mentions Publish app (7-day Testing limit). pages.js: guide step "Publish app".
- tests/integration/20-youtube-api-fallback.test.mjs: new regression test (refresh refused -> expired + hint; chat reading unaffected).
- tests/integration/harness.mjs: optional `IXC_CORE_EXE` (Windows run without Mono).
- tests/integration/02-chat.test.mjs: wait for the page's `chat.history` before the platforms send.

## Tests (Windows, IXC_CORE_EXE build, actual results)
- PASS: 02-chat (Twitch IRC, Kick+YouTube+Rumble, 5 YouTube scenarios), 19-kick-pusher (3), 20-youtube-api-fallback,
  05-obs (3), 14-obs-control, 16-routing, 17-music-norepeat (2), 12-timers (2), 13-events-import (2).
- FAIL (not fixed, not yet investigated to root cause):
  - 18-command-presets "Presets": `!followage` reply is "Follow age needs the streamer's Twitch account connected" although the test
    saw the Twitch account signed in just before. Possibly Windows-specific (account storage / DPAPI timing) - next thing to check.
  - 08-system "diagnostics export": test helper needs `python3` (not installed here) - environment, not product.
  - 06-phone, 07-accounts(?) pairing/relay tests timed out (they need Cloudflare `wrangler` local runtime; not set up here). Unverified.
- NOT RUN: 01, 03, 04, 09, 10, 11, 15, ui/*.test.mjs (Playwright browsers not installed).

## Blockers and unverified behavior
- Live Twitch/YouTube/Kick connections: no credentials or live stream available in this session - NOT verified. Only mock servers tested.
- Mono/python3/wrangler absent: part of the suite cannot run on this PC.
- OBS/Aitum multistream against real OBS: not tested.
- Stages not started: native automation, performance optimization, Windows release packaging (nothing claimed).

## Update after YouTube task
- 20-youtube-api-fallback: 2/2 pass on Windows build (mock Google/YouTube only - not real account connectivity).

## Log inspection (2026-10-19)
- %LOCALAPPDATA%IXC-OBSlogschat.log (4 lines, last 2026-10-17 04:57:28) holds only "Twitch chat connected" x2 and "Kick chat connected" x2.
  auth.log (2 lines, 2026-10-17 05:11:46 / 05:11:53) holds only "your app settings were saved (twitch id cleared)".
- NO YouTube error, warning or connection line exists in either file; no Google account was ever connected in this data folder. Nothing in the logs
  establishes a code fix, so no code was changed. The logs predate today and contain no YouTube live session.
- config: platforms.youtube = enabled, channel "infernoxc00". An ixc-core.exe (--background) is running now, but http://localhost:8767 did not answer
  (/api/ping, /api/chat/status), so its live YouTube state could not be read.

## YouTube live-discovery diagnostics (2026-10-19)
- UI evidence from user: Twitch/Kick Connected (read-only), YouTube enabled, channel infernoxc00, "Not live"; Google app "not set up", Client ID/secret empty.
  That is expected: public chat reading needs no Google app; OAuth is only for replying/moderation/API fallback. Streamer.bot not involved.
- Real check (curl, this PC, read-only): youtube.com/@infernoxc00/live returns 200 with canonical https://www.youtube.com/channel/UC..., no live markers,
  no bot check -> channel name is valid and the channel was genuinely offline at that moment.
- Defect confirmed in code: the chip maps state "unavailable" to "Not live" (ui.js), and IXC used "unavailable" for every failure to find a stream:
  bot check, a page it could not recognise (consent/redirect/layout change) and then said "your channel has no live stream" for any page without a video id,
  and a wrong channel name (404) was shown only after 2 fails as "Reconnecting".
- Fix (Platforms.cs): "not live" (state unavailable) only when the channel page was recognised (canonical /channel/UC.. or /@.., or channelId) and shows no stream.
  Otherwise state "error" with the real reason: bot check; "page wasn't recognised (consent screen, redirect, changed page)"; "YouTube has no channel X - check the name".
  YouTube viewers are no longer set to Offline when IXC could not look. Public chat reading and OAuth stay separate.
- Tests (Windows build, mock YouTube/Google only): 02-chat 7/7, 09-streamer-tools (2 run in this pass), 20-youtube-api-fallback 3/3 all pass.
  New: 20 "genuinely offline = unavailable; unreadable page / wrong name = error with reason; viewers not offline". Existing bot-check assertions (02, 09, 20) updated to expect state "error".
- Not verified: real live stream, real Google OAuth, UI rendering of the "Error" chip (ui.js already maps error -> "Error"; not visually checked).

## Supported way to test the updated core (2026-10-19)
- Build: `powershell -NoProfile -File srccoreuild-core.ps1 -Out <path>` (default out srccoreixc-core.exe; srcstart.ps1 runs it automatically when the exe is absent and then LAUNCHES IXC - do not run it while the installed IXC uses the same config/port).
- Tests never touch the user's data: the harness starts the core with `--test --config <temp>config.json` on a free port, with fake platform servers. Safe next to the installed IXC.
- buildixc-core-test.exe (481,792 bytes, built 07:48, after the last Platforms.cs edit at 07:35; untracked, not committed) was used as-is.
- Commands run from tests: `IXC_CORE_EXE=C:...uildixc-core-test.exe node --test --test-concurrency=1 integration/20-youtube-api-fallback.test.mjs` (ws installed temporarily, removed after; tracked tests/node_modules entry restored).
- Result: 3 tests, 3 pass, 0 fail (includes the offline = "unavailable" vs unreadable page / wrong name = "error" test). Mock servers only.
- Installed IXC (%LOCALAPPDATA%IXC-OBSappcoreixc-core.exe) NOT replaced, config NOT touched, no second core launched. Updating the installed copy needs your approval and a backup (e.g. copy the exe and config.json first, stop IXC, then replace).

## Installed core replaced (2026-10-19, user-approved)
- Backup first (hash-verified): DesktopIXC-backup-20261019-091804ixc-core-original.exe (480,256 B, 10/15) + data{config.json,secrets.dat,music.json}.
- No ixc-core was running at replace time. Copied buildixc-core-test.exe over %LOCALAPPDATA%IXC-OBSappcoreixc-core.exe (SHA-256 match), started it with --background (pid 9944).
- It is running but not listening on 8767 and wrote no log line: consistent with 3.2.0 "start and stop with OBS" (tray only until OBS opens; OBS is not running). NOT yet verified that the new core serves/YouTube chip shows the new status.
- Rollback: stop ixc-core, copy ixc-core-original.exe back over the installed exe. Config/secrets untouched.

## Installed core verified running (2026-10-19 ~09:30)
- http://127.0.0.1:8767/api/ping -> ok, version 3.2.0, chat+music true. Watcher pid 9944 (--background) + worker pid 1984 (--worker), both the replaced exe.
- /api/chat/status: twitch connected, kick connected, youtube state=unavailable ("Not live") detail "YouTube says the stream is not live - IXC checks again every 30 s" (page names a video that is not live and no running live chat; not a bot check / unreadable page / wrong name -> consistent with the channel being offline).
- Still unverified: YouTube while actually live; Google OAuth sign-in.

## ROOT CAUSE of "YouTube Not live while live" (2026-10-19 ~09:50)
- Evidence: public page youtube.com/@infernoxc00/live showed the live stream XgiyokD0HcA (isLiveNow:true; live_chat page has a continuation) while IXC stayed
  "unavailable / YouTube says the stream is not live". IXC's own Http.Request (loaded by reflection) also saw the live markers, so not UA/TLS/bot check.
  An isolated test-mode core with the real channel reproduced it; capturing its request showed it asked **/watch?v=infernoxc00**.
- Cause (Platforms.cs FindLiveOnPage): a channel value matching ^[w-]{11}$ was always treated as a VIDEO ID. "infernoxc00" is exactly 11 characters, so the
  handle was requested as a video and never as a channel. Discovery failed; the chat reader never started (no YouTube log lines, as observed).
- Fix: a bare 11-char value is tried as the channel handle first (/@name/live); only if YouTube answers 404 is it used as a video id. URLs and @handles unchanged.
- Test: 20-youtube-api-fallback new test (handle asked first, not used as video id; bare video id still works). 02-chat, 09, 20: all 14 pass (Windows build, mocks).
- Real-YouTube check, isolated test-mode core (temp config, free port, no accounts, NOT the installed core): channel infernoxc00 -> state connected within 5 s,
  log "YouTube chat connected (XgiyokD0HcA)", chat history contained 3 YouTube messages (e.g. "hello", "morning bro"; marked old = already in chat, not read out).
- Fixed build: buildixc-core-handle-fix.exe (untracked). The INSTALLED core (replaced earlier with the previous build) still has the bug - replacing it again needs your approval
  (backup of the original already exists on the Desktop; back up the current installed exe too).
- Workaround until then: set the YouTube channel to "@infernoxc00" (with @) - not applied (config untouched).

## Installed core replaced with handle fix (2026-10-19 09:58, user-approved)
- Previous installed exe backed up to DesktopIXC-backup-20261019-091804ixc-core-previous-build.exe (hash match); original still there as ixc-core-original.exe.
- Installed exe = buildixc-core-handle-fix.exe (hash match). Restarted (watcher 3088 + worker 8020). Config untouched.
- Live result: /api/chat/status youtube=connected, twitch=connected, kick=connected; chat.log "YouTube chat connected (XgiyokD0HcA)" at 09:58:30.
- Not yet checked: a NEW message typed in YouTube chat appearing in IXC Chat.

## Stage 1 - outgoing replies (2026-10-19)
- Trace: the message comes from Chat.Send (Chat.cs): Platforms.SendTargets() is empty because no platform has CanSend (no developer app, no signed-in account). It is the EXPECTED guard,
  not an extra defect. Defect found: the text was generic ("add Your apps ... then connect") and each platform's SendNote guessed "sign in" even when the app itself was missing.
- Existing code reviewed, no change needed: Accounts.cs (Twitch device flow, Google/Kick PKCE with the streamer's own app, DPAPI secrets, Refresh with 400/401 -> MarkInvalid, Api() retry-once on 401),
  sends: Twitch IRC PRIVMSG (chat:edit), Kick POST /public/v1/chat (429/!ok reported), YouTube liveChat/messages (quota 403 reported, needs live chat id).
- Change: Accounts.ReplyBlocker(platform,label) gives the exact next step in order: add your <Twitch|Kick|Google (YouTube)> app -> connect your account -> sign-in expired (stored reason).
  Used by Twitch/Kick/YouTube SendNote (+ Twitch missing chat:edit scope, chat not connected, sign-in refused; YouTube not live). Platforms.WhyNoReply() lists every set-up platform's step;
  Chat.Send now answers "Nothing was sent. <per-platform steps>" instead of the generic text.
- Files: src/core/Accounts.cs, src/core/Platforms.cs, src/core/Chat.cs; tests: new tests/integration/21-reply-prereqs.test.mjs, updated 07-accounts (wording), 09-streamer-tools (wording).
- Tests (Windows build, mock servers only): 21 (2/2: no apps -> per-platform steps; app but no sign-in; Kick success, 403, 429, refresh refused -> expired + nothing sent), 07 pass, 09, 11, 15, 12, 20 pass.
  Fail, unchanged/pre-existing, not Stage-1 code: 18-command-presets !followage after Twitch sign-in (Windows; cause not found), 10-quick-connect x3 (needs cloudflared/wrangler tooling not present).
- NOT verified: any real OAuth sign-in or real send on Twitch/YouTube/Kick (no developer apps are configured). Twitch IRC gives no delivery acknowledgement: rejections arrive later as NOTICE and are
  shown as errors, but a successful send is not platform-confirmed (Helix chat/messages would confirm but needs the user:write:chat scope + re-consent: NOT done). YouTube send path has no mock test of its own beyond Moderation.
- Build for this change: buildixc-core-stage1.exe (untracked). Installed core NOT replaced (still the handle-fix build).

## Multistream - step 1: destination registry + reachability check (2026-10-19)
- Existing before: OBS WebSocket client (Obs.cs ObsLink.Call, ObsControl: stream/record state, scenes, sources, replay), audio routing that READS per-platform outputs made by an OBS
  multistream plugin (Routing.cs). NO destination model, NO stream-key storage, NO output start/stop, NO relay. This PC: OBS 64-bit running; obs-plugins has no Aitum / multi-RTMP plugin;
  no ffmpeg.exe installed (only OBS's own libs). So today: ONE stream destination only (OBS's built-in service) - IXC cannot multistream yet.
- Missing for real multistream (not built): the forwarder. Options (decision needed): (A) IXC-native RTMP/RTMPS relay in C# (OBS -> rtmp://127.0.0.1 -> IXC copies the already-encoded stream to N servers;
  one encoder, no new binary, biggest to write); (B) bundle ffmpeg -c copy (needs a licence-checked ffmpeg build + 1 process per destination); (C) drive an OBS multistream plugin via obs-websocket (needs the user to install it).
  OBS WebSocket 5 cannot create outputs itself. Platform ingest rules (Kick/TikTok per-account URLs, Facebook short-lived keys, RTMPS ports) are NOT verified live; presets are starting points only.
- Built (Multistream.cs + hook in Program.cs): up to 8 destinations (platform preset, name, rtmp/rtmps server URL, enabled), stream key write-only and stored DPAPI-encrypted in secrets.dat
  (never returned by the API, never in config.json/logs/diag), URL validation (no user:pass, no ?query), edit-keeps-key / clearKey, delete; "Check" = TCP + TLS(rtmps) + RTMP handshake only (key and video are NOT sent),
  3 attempts with growing wait, cancellable (edit/delete/new check), per-destination state idle/checking/ok/failed + reason. API: GET /api/multistream, POST save|delete|check. relay.ready=false is reported honestly.
- Not built: the UI page (API only), start/stop, forwarding, OBS-streaming-linked behaviour, rtmps against a real certificate (no test cert), resource measurements.
- Files: src/core/Multistream.cs (new), src/core/Program.cs (1 line), tests/integration/22-multistream.test.mjs (new). Build: build/ixc-core-multistream-step1.exe (untracked).
- Tests (Windows build, local fake RTMP servers): 22-multistream 3/3 pass (registry+key secrecy+restart, handshake ok sends 1537 bytes and no key, drop/non-RTMP/silent/dead port failures, 3-attempt bound, recovery on try 3, cancel on delete).
  Also pass: 01 (6), 21 (2), 05 first two, 14. 05 third test ("OBS closed") FAILED this run only because OBS is now running on this PC (test needs OBS closed) - environmental, passed earlier.
- Not touched: installed core (still handle-fix build), config, running stream/connections.

## Multistream - step 2: native RTMP relay (2026-10-19)
- Path of the stream: OBS (one encoder) -> rtmp://127.0.0.1:<port>/live (IXC's loopback-only ingest, key = random local token) -> IXC forwards the SAME audio/video packets (never decoded/re-encoded) to up to 8 RTMP/RTMPS destinations.
  OBS WebSocket cannot create outputs; it can only edit the stream service. IXC does NOT edit OBS settings: the user types the shown server URL + key into OBS (Settings > Stream > Custom) - needs the user's action/approval.
- Implementation: own C# code from Adobe's public RTMP spec (src/core/Rtmp.cs): handshake, chunk reader/writer (all fmt types, extended timestamps, set-chunk-size), AMF0 commands, ack/ping/window handling, TLS via SslStream (TLS 1.2 explicit).
  No third-party library -> no licensing/packaging issue. Per destination: own thread + bounded queue (16 MB / 2000 msgs, overflow = drop and rejoin at next keyframe), 10 s connect timeout, 15 s send timeout, reconnect with exponential back-off (1 s .. 30 s), joins at a keyframe, resends description + codec headers on every (re)connect, timestamps re-based per destination, graceful FCUnpublish/deleteStream on stop, non-blocking Stop().
  Keys live only in secrets.dat (DPAPI) and in memory while connecting; never in logs/API/diag/test output (verified by test); server-supplied error text has the key masked.
- API: GET/POST /api/multistream/relay {action: start|stop}; each destination view now has relay {state idle|connecting|live|reconnecting|skipped, detail, sentMB, frames, droppedFrames, reconnects}. The relay listens only after "start" (nothing runs/uses resources while off). Save/delete apply to a running stream (that destination restarts / stops).
- Real defect found+fixed by the TLS test: SslStream.AuthenticateAsClient(host) in this exe defaulted to TLS 1.0 (exe not marked .NET 4.8) - would have failed against every real RTMPS server, incl. the earlier "Check" feature. Now TLS 1.2 explicitly (Rtmp.cs, Multistream.cs).
- Files: src/core/Rtmp.cs (new), src/core/Multistream.cs (relay hooks, TLS fix), tests/integration/rtmp.mjs (new: independent fake RTMP server + fake OBS), tests/integration/23-relay.test.mjs (new, 6 tests), 22-multistream (1 assertion updated).
- Tests (Windows build): 22 (3) + 23 (6) + 21 (2) + 01 (6) + 14 = 18/18 pass in the last run. 23 covers: one destination byte-identical + headers + ext. timestamps + chunking + key secrecy; 8 destinations identical + one rejecting (BadName) keeps retrying while 7 stream on;
  drop + reconnect + rejoin at keyframe while another is unaffected; OBS stop / delete destination / relay stop / wrong ingest key / second publisher / port in use / skipped destinations; rtmps with a throw-away certificate (certificate check disabled via the test-only switch).
- Measured (one run, loopback, 8 fake destinations in the same PC, ~3.6 Mbps in = 28.7 Mbps out, 15 s): core RAM 45.8 MB idle -> 49.4 MB streaming, 58 threads, ~1.7% of one core, 0 dropped frames. Not a real-network or real-OBS measurement.
- NOT verified / not built: any real platform ingest (Twitch/YouTube/Kick/Facebook/TikTok/Rumble - URLs, key rules, per-account endpoints, enhanced-RTMP/HEVC acceptance, RTMPS certificates); real OBS publishing to it (OBS's librtmp may behave differently from the fake: please test with the pair of settings above on a throw-away stream first);
  a slow destination overflowing its queue (logic exists, untested); UI page for destinations + relay (API only); auto-start with IXC; reconnect of OBS mid-stream beyond a fresh session; adding/removing destinations beyond restarting that destination.
- Installed core NOT replaced (still handle-fix build); config and running stream untouched. Test build: build\ixc-core-relay.exe (untracked).

## Multistream - step 3: dashboard page (2026-10-19)
- UI architecture: static dashboard in src/app (app.js nav + pages.js `page(id, title, fn)`, `IXC.api`, `App.modal`, `UI.pill/confirmBox/toast`), served by the core. New page "Multistream" (nav: Setup > Multistream) uses only /api/multistream*.
- Page: Relay card (Off / On-waiting for OBS / OBS sending, Start/Stop; when on: IXC ingest server URL + local ingest key, masked until "Show", Copy buttons, text that IXC does NOT edit OBS and the relay never auto-starts);
  up to 8 destination cards (platform, editable name, server URL, write-only stream-key box, enabled switch, Check server / Edit / Delete with confirmation, "+ Add destination" disabled at 8 with the limit shown);
  two separate status lines per destination: "Server reachable (handshake only - does not prove the key is accepted)" vs "Publishing - the server accepted the stream (N MB sent)", with honest alternatives (Connecting / Not publishing: <reason> / Not used: no key / turned off / Waiting for OBS / relay off).
- Keys: typed in a password field, sent once, field emptied (also after a failed save), stored DPAPI-encrypted; GET /api/multistream only returns keySet; page shows "Key saved - hidden"; editing shows an empty box + "saved - leave empty to keep it", "Remove the saved key" checkbox. Tests assert the key is absent from the DOM, page source, API, diag, settings, logs, secrets.dat bytes.
- Presets: no pre-filled addresses except three the platforms document themselves (Twitch, YouTube, Facebook), marked "Suggested address - confirm it in the platform's dashboard"; Kick, TikTok, Rumble, Trovo (URL removed) and Custom start empty; any rtmp:// or rtmps:// URL is accepted.
- Files: src/app/pages.js (new page), src/app/app.js (nav entry), src/core/Multistream.cs (preset flag "suggested", Trovo URL removed), tests/ui/multistream.test.mjs (new, 3 browser tests). Build: build\ixc-core-multistream-ui.exe (untracked).
- Tests (Windows; Playwright 1.x installed temporarily without its browsers, driving the Edge that Windows installs - set IXC_TEST_BROWSER to override; tests/node_modules restored): ui/multistream 3/3 pass (add / bad address error / edit keeps key / switch / remove key / delete; 8-destination limit incl. disabled button; relay start/stop, check-server vs publishing, rejecting server reason BadName, port-in-use error, no JS errors).
  Integration re-run after the preset change: 20, 21, 22, 23 = 15/15 pass. The older ui/ui.test.mjs (all pages) was NOT run (needs Playwright's Chromium) - the nav gained one entry.
- Not done: auto-start (deliberately off), real OBS and real platform publishing, slow-destination overflow test, status shown on Home. Installed core NOT replaced; config / OBS untouched.

## Installed core replaced with the multistream UI build (2026-10-19, user-approved)
- OBS was open but NOT streaming (obs state streaming=false) when swapped. Backups on Desktop\IXC-backup-20261019-091804: ixc-core-handle-fix-installed.exe (hash match), data\config-before-multistream-ui.json, data\secrets-before-multistream-ui.dat; originals from earlier still there.
- Installed exe = build\ixc-core-multistream-ui.exe (hash match); watcher pid 16084 restarted, worker came up; /api/ping ok 3.2.0, /api/multistream ok (0 destinations, relay off), Twitch + Kick connected.
- YouTube after restart: unavailable, "your channel has no live stream right now" (channel page recognised, no live stream at that moment - the new accurate offline message). OBS and its streaming settings were not touched; no destinations configured; relay not started.
- Rollback: stop ixc-core, copy ixc-core-handle-fix-installed.exe back over the installed exe.

## Architecture correction + read-only investigation (2026-10-19, no code or files changed)
- C. Why "Multistream" is missing from the UI: the dashboard is NOT inside ixc-core.exe. It is static files served from %LOCALAPPDATA%\IXC-OBS\app\app\ (app.js, pages.js, index.html ... all dated 10/15/2026 7:06 PM).
  I replaced only app\core\ixc-core.exe (10/19 10:50, 514,560 B). Evidence: the running core (pid 12224 worker, 16084 watcher, both from the installed path) serves /app/app.js with 0 "multistream" mentions and /app/pages.js with 1 (old audio-routing text);
  the repo src/app has 1 and 10. API works (/api/multistream answered) - only the UI files are old. A second stale copy exists at %LOCALAPPDATA%\IXC-OBS\setup\src\app\ (installer source). Fix = copy src/app/* over app\app\ (after backing up the 5 files); then reload the dashboard / OBS dock (browser cache). Not done yet - needs the user's go-ahead.
- B. Aitum Multistream: NOT installed. OBS Studio 32.2.2 (C:\Program Files\obs-studio), obs-websocket 5.7.4 on port 4455 (enabled, IXC connected). obs-plugins\64bit has only OBS's stock modules; %APPDATA%\obs-studio\plugins and C:\ProgramData\obs-studio\plugins do not exist; the OBS log lists no Aitum module.
  Web (forum/GitHub, not verified by running it): Aitum Multistream is GPL-2.0, Windows installer + zip published (releases 1.0.6/1.0.7/1.0.8), 64-bit only, forum says minimum OBS 30; release notes mention OBS 31 fixes, NO statement about OBS 32; a forum report says 1.0.8 failed on a portable OBS 30.2.3 where 1.0.7 worked.
  Control interface: README documents none. The repo contains obs-websocket's vendor-API header (so vendor requests are possible) but whether/what it registers is unknown, and forum users report no way to start its secondary outputs via obs-websocket. Candidate supported routes, ALL UNVERIFIED until it is installed: (1) obs-websocket GetOutputList / GetOutputStatus / StartOutput / StopOutput if Aitum's outputs appear as OBS outputs;
  (2) GetHotkeyList + TriggerHotkeyByName if it registers hotkeys; (3) CallVendorRequest if it registers a vendor. Otherwise IXC can only show OBS's own stream state, and the user starts Aitum outputs in its dock.
- A. Streamer.bot: closed-source, website terms (streamer.bot/terms) say "You may not duplicate, copy, or reuse any portion of the integration code ... or concepts" without written permission; nothing grants redistribution/embedding -> do NOT bundle or embed it. IXC's own implementations (already: commands, timers, events, variables in commands, OBS control, TTS, music, per-platform chat) stay native; its WebSocket bridge stays optional only.
  Setup minimisation (plan, not built): public chat reading already needs no account/app on Twitch, Kick, YouTube. Replies/moderation need OAuth by each platform's rules: Twitch device-code flow needs only a public client ID (could be built in = one click); Google needs a client ID + secret and, for the sensitive youtube scope, app verification (else "Testing" = 100 users, 7-day sign-in); Kick needs client ID + secret. Hence today's per-user "Your apps".
- Custom RTMP relay: frozen. Code (Rtmp.cs, Multistream.cs, Multistream page, tests 22/23, ui/multistream) stays in the tree, dormant (relay off by default, no auto-start); no further expansion unless Aitum cannot satisfy a concrete requirement.
- State unchanged: installed core = multistream-ui build, config/secrets/backups untouched, Twitch/Kick connected, no plugin installed.

## Task 1 done - Multistream UI restored in the running dashboard (2026-10-19)
- Backed up all 5 installed UI files (app.css, app.js, index.html, pages.js, wizard.js) to Desktop\IXC-backup-20261019-091804\app-ui-before-multistream\.
- Verified structure/version: installed UI == git HEAD for all 5 files (3.2.0, same as VERSION); installed core\web\ui.js and ui.css == repo. Working tree differs from HEAD only in app.js (nav entry) and pages.js (Multistream page + Google "Publish app" guide text) - so only those 2 files were copied to %LOCALAPPDATA%\IXC-OBS\app\app\ (hash match after copy). ixc-core.exe, config, secrets, other files untouched.
- Verified in a real browser (Edge, Playwright script from a scratch folder, NOT the repo) against the RUNNING installed IXC at http://localhost:8767/app/: nav lists "Multistream" (between OBS and Audio routing), clicking it opens #multistream with Relay (Off, Start relay) and Destinations (0 of 8, + Add destination); 0 console/page errors. Nothing was clicked that changes state.
- Rollback: copy the 5 backed-up files back.

## Task 2 prep - Aitum Multistream candidate (read-only; NOTHING installed)
- Official source: github.com/Aitum/obs-aitum-multistream releases (also linked from the OBS forum resource page). Latest: 1.0.8, published 2026-05-18, not a prerelease, GPL-2.0. Windows assets: aitum-multistream-windows-installer.exe (3,645,344 B, sha256 be27a661...2636), aitum-multistream-windows-programdata.zip (2,555,735 B, sha256 892d03e200e7cfaeeb43c410d75749ee0bddb9913259f0becc76bc4a3182719d), aitum-multistream-windows.zip (2,555,877 B, sha256 0d6c3e3466a1dab20f6000b78178a7b30040b9e9ec81daf66948f237c246472c).
- Compatibility with OBS 32.2.2: NOT stated anywhere official. OBS forum page: minimum OBS 30.0.0, no maximum, no OBS 32 statement; release notes only mention OBS 31 (1.0.4/1.0.6). 1.0.8 post-dates OBS 32 (Sept 2025) so it is plausibly built for it, but that is an inference. Only a real load test on this PC decides.
- Proposed (needs user approval of the version + method): 1.0.8 via aitum-multistream-windows-programdata.zip, checking the sha256 first, extracted to C:\ProgramData\obs-studio\plugins (no installer exe, no admin needed for the zip itself, removable by deleting that folder). OBS must be restarted to load it (OBS is open now, not streaming).

## MASTER INTEGRATION AUDIT + PLAN (2026-10-19) - read-only, nothing installed/changed
Facts gathered
- IXC 3.2.0 (MIT): C# core (csc, .NET Framework, no dependencies), static dashboard src/app, per-user Inno installer (PrivilegesRequired=lowest, installs to %LOCALAPPDATA%\\IXC-OBS, can enable OBS WebSocket + add docks while OBS is closed), 23 integration test files + Playwright UI tests. Work since 3.2.0 is UNCOMMITTED on branch claude/ixc-stability-20261019.
- Installed: core = multistream-ui build (not a released build), UI app.js/pages.js updated (Multistream nav verified in a real browser), watcher+worker running, Twitch+Kick connected, YouTube offline. Backups: Desktop\\IXC-backup-20261019-091804 (original core, previous builds, config/secrets copies, UI files).
- OBS 32.2.2 x64 (Program Files), obs-websocket 5.7.4 on 4455 WITH authentication (verified: auth required) - IXC connects with the password from OBS's config. I did not extract/use that password, so obs-websocket's live request list is NOT yet verified (Stage 0). Plugins: stock only; no Aitum.
- Aitum Multistream (GPL-2.0, repo pushed 2026-10-08, latest release 1.0.8 2026-05-18, 4 open issues). Source (main branch, read via GitHub raw; NOT tested on the installed binary): obs-websocket vendor "aitum-multistream" with requests version, get_outputs, start_output, stop_output and events output_started, output_stopped; no hotkeys; each output can reuse the main encoder or have its own. NO request to add/edit/reorder outputs, set keys or encoder settings.
- Aitum Vertical Canvas (repo obs-vertical-canvas, GPL-2.0, 1.6.6 released 2026-10-06; 1.6.5 2026-10-05). Vendor "aitum-vertical-canvas": version, switch_scene, current_scene, get_scenes, status, start/stop/toggle_streaming, start/stop/toggle_recording, start/stop/save_backtrack, start/stop_virtual_camera, update_stream_key, update_stream_server, add_chapter, pause/unpause_recording (first 100k chars of main file read; list may be incomplete). No layout/source editing requests. Win assets: installer.exe, windows.zip, windows-programdata.zip (sha256 for 1.6.6 in the GitHub API; programdata zip eba2448e...85e7, windows zip 6bfba04e...f8d3, installer 01a81d1e...1fd8).
- Streamer.bot: closed, terms forbid copying its integration code/concepts without written permission -> native equivalents only, bridge stays optional.
Feature matrix and staged plan: see the assistant message of this date. Decisions awaiting the user: (1) approve Aitum Multistream 1.0.8 + Vertical Canvas 1.6.6 install method; (2) accept that destination add/edit/keys/encoders are NOT remotely controllable via Aitum's vendor API (IXC = status + start/stop; edits in Aitum's dock) or choose another route; (3) custom relay: keep dormant or remove; (4) bundle unmodified GPL binaries in the IXC installer with source offer; (5) commit current work locally; (6) Stage 0 core swap approval; (7) platform app/credential approach.

## STAGE 0 (approved) - read-only OBS capability report (2026-10-19)
- Built separately: C:\Users\Administrator\.claude\jobs\271db1e1\tmp\core\ixc-core.exe -> copied to build\ixc-core-stage0.exe (untracked). Installed core NOT replaced (still the multistream-ui build, hash 964441636C49...).
- New (read-only): ObsControl.Capabilities() + GET /api/obs/capabilities (for the installed core after an approved swap) + `ixc-core.exe --obs-capabilities <report.json> --config <scratch>\config.json` probe mode (starts ONLY the OBS link: no setup, routing, docks, chat or web server; refuses to run without an explicit scratch --config; writes nothing to OBS). Sends only look-up requests: GetVersion, GetOutputList/Status, GetVideoSettings, GetStream/RecordStatus, GetProfileList, GetHotkeyList, GetStats, CallVendorRequest "version" for the two Aitum vendors. Deliberately never calls GetStreamServiceSettings / GetOutputSettings / GetProfileParameter (they can contain stream keys). Fields are allow-listed, so unexpected keys are never copied.
- Relay hidden: Multistream nav item removed from app.js (page code, API, relay, tests all kept; relay is off by default, no auto-start).
- Also: Links.cs logs the raw connection error at debug level (the friendly message hid the real cause).
- REAL report (probe against the user's running OBS via OBS's own WebSocket settings, scratch config, exit 0; copy: Desktop\IXC-backup-20261019-091804\obs-capability-report-2026-10-19.json, 9.7 KB, no key/password fields):
  OBS 32.2.2, obs-websocket 5.7.4, RPC 1, Windows 11 24H2. 151 requests. Video: base 1920x1080, output 1920x1080, 60/1 fps. Streaming/recording: off. Profile "Untitled". OBS CPU 2.6 %, 197 MB, 60 fps, 0 skipped frames.
  Outputs (3, all stock): simple_stream (rtmp_output, 1920x1080, service, multi-track), simple_file_output (mp4_output), virtualcam_output. No Aitum outputs. Vendors "aitum-multistream" and "aitum-vertical-canvas": not registered ("No vendor was found by that name") = plugins not installed. 72 hotkeys, none Aitum.
  Requests that matter: Start/Stop/Toggle/GetStatus for Stream, Record, ReplayBuffer, VirtualCam; GetOutputList / GetOutputStatus / StartOutput / StopOutput / ToggleOutput / GetOutputSettings / SetOutputSettings (any registered output, by name); GetStreamServiceSettings / SetStreamServiceSettings (service + server + KEY: handle with care); GetVideoSettings / SetVideoSettings (global base/output resolution + fps); GetProfileParameter / SetProfileParameter / CreateProfile / SetCurrentProfile; SceneItem transform/enable; source filters; GetHotkeyList / TriggerHotkeyByName; CallVendorRequest. Canvas: ONLY GetCanvasList (read-only) - OBS 32 knows canvases, but obs-websocket 5.7.4 has no request to create or edit them or their scenes.
- Plugin facts verified WITHOUT installing (GitHub sources/API): Aitum Multistream 1.0.8 (GPL-2.0) is built against OBS 31.0.0-fix1 (exeldro fork), Qt6 prebuilt 2024-09; Vertical Canvas 1.6.6 (GPL-2.0) against OBS 31.1.0, Qt6 2025-05. The installed OBS is 32.2.2 (Qt 6.11.1): neither plugin states OBS 32 support; same-major-Qt and OBS's plugin-compat policy make loading plausible, but it is UNVERIFIED and OBS 32 is a new major version. Only an actual load test decides (needs approval).
  Control interfaces (source): multistream vendor = version, get_outputs, start_output, stop_output + events output_started/stopped; vertical vendor = version, status, scene get/switch, stream/record/replay/virtualcam start/stop/toggle, update_stream_key, update_stream_server, chapters, pause. Neither offers add/edit/remove/reorder of destinations, encoder, bitrate, resolution, keys (multistream) or layout editing. Can IXC manage destination + encoder config reliably? NOT through any supported interface found. Undocumented config files: not touched (user rule).
  Licensing: both GPL-2.0 -> shipping unmodified official binaries with licence text + source offer is permitted as aggregation; IXC stays MIT (talks only over WebSocket). Aitum branding/trademark terms not stated in the READMEs; plugins have an update-check dialog (network call).
- Tests (Windows build, fake OBS; real-OBS run above is the only real integration): 24-obs-capabilities 2/2 (report content, field allow-listing, only look-ups sent, forbidden requests never sent, probe mode writes the report, refuses missing scratch --config with exit 64, reports unreachable OBS with exit 2). Regression run: see below.
- Rollback: nothing installed changed. To discard Stage 0: `git revert` the Stage 0 commit (or reset the branch to the previous local commit) and ignore build\ixc-core-stage0.exe; the installed core/UI rollback files are in Desktop\IXC-backup-20261019-091804.

- Regression (Windows build ixc-core-stage0.exe, fake servers, final state): 62 tests, 61 pass, 1 fail. Suites run: integration 01,02,03,04,05,07,09,11,12,13,14,15,16,17,19,20,21,22,23,24 + ui/multistream. The one failure is "OBS: turning on the WebSocket server and adding the panels (OBS closed)": it needs OBS NOT running and OBS is open on this PC (environmental, passed earlier when OBS was closed).
  NOT run: 06 phone + 10 quick-connect (need Cloudflare/wrangler tooling), 08 diagnostics export (needs python3), 18 command presets (known pre-existing !followage failure), ui/ui.test.mjs + other UI tests (need Playwright's Chromium; only the multistream UI test ran, through the Windows Edge). Nothing was run on Mono/CI: the new C# is written for C# 5 but was only compiled with the Windows csc.
- Probe build: C:\...\tmp\core\ixc-core.exe == build\ixc-core-stage0.exe (SHA-256 begins 046dc9e9e83a07a0).

## Stage 0 confirmation + swap plan + design notes (2026-10-19, nothing swapped/installed)
- ixc-core-stage0.exe == the tested build: 523,264 bytes, SHA-256 046DC9E9E83A07A0FB9357F5DDE6F242145A83683EF080B2A9AF5B604B427455, identical to the exe that ran the regression, the fake-OBS tests and the real probe. Working tree = commit d0e0949 (0 tracked changes). A fresh rebuild from the committed tree gives the same size (csc output is not byte-reproducible, so the hash differs; that is expected).
- Report endpoint is read-only: the only OBS requests in Capabilities() are GetVersion, GetOutputList, GetOutputStatus, GetVideoSettings, GetStreamStatus, GetRecordStatus, GetProfileList, GetHotkeyList, GetStats and CallVendorRequest(requestType "version") for the two Aitum vendors (grep-verified: no Set/Start/Stop/Create/Remove/Toggle/Trigger/Save names); GET only, behind the core's existing origin/rebinding guard; tests assert only look-ups reach OBS and that key-bearing requests are never sent.
- Hidden nav cannot affect chat/OBS: src/app/app.js is byte-identical to 3.2.0 (git diff empty); pages.js only adds a page that is registered but never run unless #multistream is opened (74 added lines, 1 changed guide-text line); the relay is off by default and starts nothing on its own; Links.cs change = one debug log line. NOTE: the INSTALLED dashboard still has the Multistream nav item (app.js from Task 1, sha256 A0431CDA...) - the swap procedure restores the original app.js (sha256 DFBDD4AB1C607059E134D01E4EBC0ED6CE2EFE9057B6B6C3388197B5A3596B74, same as the backup).
- Installed now: core sha256 964441636C49BC2E0E8DB355234EA117318B36BE86F0869AE7EFBA98C634AD9C (multistream-ui build); pages.js == repo.
- obs-websocket protocol docs (master): GetCanvasList; canvasUuid parameter on GetSceneList, GetSourceActive, Get/SaveSourceScreenshot; GetCurrentProgramScene does NOT support canvases; there is no request to create/size a canvas or to create outputs (outputs: Get/Start/Stop/Toggle/GetSettings/SetSettings of EXISTING ones only). Whether 5.7.4 on this PC supports canvasUuid is unverified (only GetCanvasList was seen in its request list).
- Design (see assistant message): MultistreamBackend adapter with capability flags (IxcRelay = native CRUD/keys/start-stop/status/profiles but one shared encode; AitumVendor = discover/status/start/stop only; future OBS-side companion = per-output encoder + CRUD). Vertical needs an OBS-side component (Aitum Vertical or an IXC companion) or a second OBS instance (--multi); status-only is NOT vertical-canvas support. Automation Studio = native engine in the core, existing commands/timers/events kept running until parity tests pass.
- Awaiting: approval of the specific Stage 0 swap (core exe + restoring original app.js), then Stage 1 shells.

## Safety checks A + architecture comparison B/C/D (2026-10-19) - nothing swapped, nothing installed
- A: Stage 0 exe sha256 046DC9E9E83A07A0FB9357F5DDE6F242145A83683EF080B2A9AF5B604B427455, built from source tree == commit d0e0949 (git diff d0e0949 -- src/core empty; HEAD fd8c69e only adds status notes). Pre-swap backup created (copy only): Desktop\IXC-backup-20261019-091804\pre-stage0-swap\ = ixc-core.exe (964441636C49...), app.js, pages.js, config.json, secrets.dat, music.json + SHA256.txt; all equal to the live files. Rollback rehearsed in a scratch copy: swap -> stage0 core + original app.js; rollback -> hashes identical to the pre-swap files and to the currently installed core. Earlier backups all present. Running processes untouched (16084 watcher, 12224 worker).
- app.js: NOT needed by the Stage 0 diagnostic exe (the exe never reads dashboard files; /api/obs/capabilities has no UI). Restoring the original app.js only removes the Multistream nav item from the installed dashboard (the item was added by Task 1). It is optional and separate from the core swap.
- This PC: Ryzen 7 5800X3D, RX 6600 8 GB (driver 32.0.21045), 16 GB RAM. OBS profile "Untitled": Simple mode, x264 veryfast, 8750 kbps video, 160 kbps audio, 1920x1080 @ 60 (log earlier showed 1280x720 @ 30). OBS lists AMD hardware encoders h264_texture_amf and h265_texture_amf (no NVENC) plus SVT-AV1/AOM-AV1 software.
- B relay audit (code + tests): pass-through only. Codecs: any codec travels untouched (H.264/HEVC/AV1 incl. enhanced-RTMP framing); keyframe + sequence-header detection exists for legacy AVC, enhanced video and AAC only (other audio codecs would not get their header replayed on a reconnect: untested). One audio track. NO transcoding, so NO independent encoder, bitrate, resolution, fps, keyframe interval or audio settings per destination - every destination receives OBS's single encode; the relay does not report bitrate/health beyond MB sent, skipped frames, reconnects. Reconnect: exponential back-off 1..30 s, rejoin at keyframe, headers replayed, bounded queue (16 MB / 2000 msgs), per-destination timeouts. Measured (loopback, fake servers, 8 outputs): 46 -> 49 MB RAM, ~1.7 % of one core at 3.6 Mbps in / 28.7 out, 58 threads. Not measured: real network, real OBS.
- Options for independent per-destination settings (without touching Aitum files): (1) IXC transcoder (decode + re-encode in IXC with a bundled media library) - works anywhere but costs decode + one encode per destination and needs ffmpeg-class libraries (LGPL dynamic or GPL, ~80 MB, licence review); (2) OBS-side output with its own encoder = Aitum Multistream (settings only in its dock) or an IXC companion OBS plugin exposing documented-by-us vendor requests (CRUD + encoder per output) - OBS renders once, only the encode is extra; (3) hybrid (recommended): relay for "same as OBS" destinations, OBS-side outputs only for the few destinations that need different settings. Expected cost (ESTIMATES, not measured; to be measured with OBS stats in an approved test): each extra x264 veryfast 720p30 output ~3-6 % of this CPU; each AMF H.264 output uses the RX 6600's video-encode block with a small 3D/VRAM cost (hundreds of MB) but lower quality per bit than x264; a full-HD 60 fps extra x264 output is much heavier. A transcoder adds a decode (~2-5 % CPU, or GPU decode) before each encode.
- C vertical: Aitum Vertical Canvas 1.6.6 = canvas + scenes + its own encoder/outputs inside OBS, can share the main canvas's sources (no second capture), vendor API for control; cost: one extra render + one encode (e.g. 1080x1920 @ 30); built against OBS 31.1.0 (OBS 32.2.2 compatibility unverified); GPL-2.0; layout editing only in its OBS docks. Companion plugin: same pipeline but ours (C++/libobs, build + signing per OBS major, full control incl. layout/output config), much more work. Second OBS instance (portable copy + 1080x1920 profile + own websocket port): full documented control, no plugin; cost: ~300-600 MB RAM, duplicate capture/render of every source (game capture/capture cards may not be shareable), second encoder, a portable OBS copy in the user's folder (hundreds of MB, matching version), two UIs to keep consistent; automatic installation = copying the installed OBS folder + portable marker (no download). Recommendation: Aitum Vertical (OBS-side) for the pipeline, IXC for control/status/presets/preview(if canvas screenshots work)/installer bundling; keep the companion plugin as a later option. Needs approval + a load test on OBS 32.2.2 before anything is claimed.
- D automation: unchanged design (native engine in the core; existing commands/timers/event messages keep running; no Streamer.bot process/code).

## Next action
Next smallest diagnostic: while actually live on YouTube, open IXC > Platforms and note the YouTube chip text (Not live / bot check / Connected / error),
then IXC > Updates & diagnostics > Export diagnostics (redacted zip) and read the YouTube lines in chat.log. If the port differs from 8767, use the one
shown in IXC's tray/app.
(Previous next-actions below still apply.)

1. Investigate 18-command-presets followage failure (Accounts.Has after /api/accounts/twitch/connect).
2. Run remaining integration files (01,03,04,09,10,11,15); install python3 or port zipText helper.
3. Follow docs/LIVE-TEST-CHECKLIST.md on a real stream for Twitch/YouTube/Kick.

## Rules
Record actual evidence only. Do not mark unverified features complete.
