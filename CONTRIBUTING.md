# Contributing to IXC ChatBox

Thanks for helping! Bug reports, ideas and pull requests are welcome.

**Bugs:** use the *Bug report* issue form and include your Windows and OBS versions, the steps, `%LOCALAPPDATA%\IXC-OBS\helper.log`
and `chat-relay.log`. Never post passwords, keys or your config.json.

**Development**
```powershell
git clone https://github.com/infernoxc/ixc-chatbox.git; cd ixc-chatbox
powershell -ExecutionPolicy Bypass -File tests\smoke.ps1          # offline checks (~20 s)
powershell -ExecutionPolicy Bypass -File src\helper\ixc-helper.ps1 # run from source on port 8767 (stop an installed copy first)
```
Pages in `src/chat` are plain HTML/JS. Open `http://localhost:8767/chat/chat.html?dock=1&demo=1` to work without Streamer.bot. `ChatRelay.cs` must stay **C# 5** (it is compiled by the .NET Framework compiler built into Windows).

**Pull request rules**
- Windows PowerShell **5.1** compatible (no PowerShell 7-only syntax); no new runtime dependencies or downloads.
- No personal paths, names, keys or tokens (the smoke test checks common cases).
- Keep it light on CPU. Run `tests\smoke.ps1`, and add a check for new behaviour where possible.
- Add a line under **[Unreleased]** in `CHANGELOG.md`.
- The helper is shared with [IXC Music](https://github.com/infernoxc/ixc-music): keep `src/helper` identical in both repos.

By contributing you agree that your contribution is licensed under the MIT License.