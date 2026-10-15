# IXC 3.2.0 - live test checklist (about 15 minutes, on your PC, during a real stream)

These are the checks that automated tests can't do: they need your accounts, your OBS and a real stream.
Write down **pass / fail** for each line. If something fails: IXC > Updates & diagnostics > **Export diagnostics**
(no passwords or tokens inside) and send the zip.

## Before you go live
1. Install `IXC-Setup-v3.2.0.exe` over your current IXC. Your settings stay (a backup is made first).
2. Close OBS. Sign out of Windows and back in (or restart). Only the IXC tray icon should start.
3. Open OBS. Within ~5 s IXC starts by itself (tray tooltip changes from "starts when OBS opens").
   Task Manager: two `ixc-core.exe` (tray + IXC), not more.
4. IXC > OBS: shows **Connected**. Your scenes are listed; nothing in OBS changed except the IXC sources.

## Kick (with your Kick account connected under Platforms & accounts)
5. Go live on Kick. Type a message in your Kick chat from another account or phone: it appears in the IXC chat dock.
6. Type `!np` there: IXC answers in Kick chat (needs your Kick app under Your apps).
7. With the chat voice on, a Kick message is read out.

## YouTube
8. Go live on YouTube. Within ~1 minute the YouTube chip is **Connected** and a test message appears.
   If it says "confirm it's not a bot": connect your YouTube account (Platforms & accounts) - IXC then asks
   YouTube's official API for your live stream instead.

## Twitch
9. Type `!followage` from a viewer account that follows you: it answers with the time.
10. While live, type `!clip`: a clips.twitch.tv link appears in chat. (Sign in to Twitch again in IXC first if it
    says it needs the clips permission.)

## Audio per platform (needs one OBS output per platform, e.g. Aitum Multistream, each on its own audio track)
11. IXC > Audio routing: switch **Music** off for Kick. Play a song. Kick viewers don't hear it; Twitch and YouTube do.
12. Switch it back on. Do the same for **Chat voice**. The phone remote shows the same switches.

## After the stream
13. Close OBS. About 20 s later IXC stops; the tray icon stays. Open OBS again: IXC comes back, music and routing as before.
