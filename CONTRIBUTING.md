# Contributing to IXC

Thanks for helping! Bug reports, ideas and pull requests are welcome.

**Bugs:** use the *Bug report* issue form. Attach the ZIP from **IXC > System check > Export diagnostics**: it has the logs and system details, and no passwords or keys. Never post your `config.json` or `secrets.dat`.

## Development
```powershell
git clone https://github.com/infernoxc/ixc-chatbox.git; cd ixc-chatbox
powershell -ExecutionPolicy Bypass -File src\core\build-core.ps1    # builds src\core\ixc-core.exe (C# compiler built into Windows)
powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -Install   # Windows tests: build, run, install/update/uninstall
```
Behaviour and UI tests (Linux, WSL or CI; needs Mono and Node 20+):
```sh
cd tests && npm install && npx playwright install chromium && npm test
```
They run IXC against fake Twitch / Kick / YouTube / Rumble / OBS / Streamer.bot / GitHub servers, and run the real phone relay in Cloudflare's local runtime.

## Rules
- `src/core/*.cs` must stay **C# 5**. It is compiled by the .NET Framework compiler built into Windows, and the Linux build uses `-langversion:5` to check that.
- Windows PowerShell **5.1** compatible scripts. No new runtime dependencies for users.
- Every setting goes through `Settings.cs` (type, default, limits). Secrets go through `Secrets`, never into `config.json` or logs.
- No personal paths, names, keys or tokens (the smoke test checks common cases).
- Add or extend a test for new behaviour, and a line under **[Unreleased]** in `CHANGELOG.md`.

By contributing you agree that your contribution is licensed under the MIT License.
