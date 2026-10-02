# Releasing

**Versioning:** [SemVer](https://semver.org) `MAJOR.MINOR.PATCH`. The version is in `VERSION`, each release gets a tag `vX.Y.Z`,
a `CHANGELOG.md` section and `docs/release-notes/vX.Y.Z.md`.

1. Update `VERSION`, `CHANGELOG.md` and the release notes.
2. Test and build locally:
   ```powershell
   cd tests; npm install; npx playwright install chromium; npm test; cd ..   # behaviour + UI tests (Linux/WSL with Mono, or CI)
   powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -Install    # Windows: build, run, install/update/uninstall
   powershell -ExecutionPolicy Bypass -File build\build.ps1 -Installer   # needs Inno Setup 6 (winget install JRSoftware.InnoSetup)
   ```
3. Commit, tag and push:
   ```powershell
   git commit -am "Release vX.Y.Z"; git tag -a vX.Y.Z -m "vX.Y.Z"; git push; git push origin vX.Y.Z
   ```
4. The **Release** workflow builds on a clean Windows runner (with the repository variables from [DEVELOPER-SETUP.md](DEVELOPER-SETUP.md)), tests the setup program on that clean machine, then publishes the GitHub Release with the ZIP, the Setup
   `.exe` and `SHA256SUMS.txt`. To do it by hand: `gh release create vX.Y.Z dist\* --title "IXC vX.Y.Z" --notes-file docs\release-notes\vX.Y.Z.md`
5. Download the release on a clean PC (or Windows Sandbox) and follow docs/INSTALL.md with a real OBS and real accounts (see the manual checklist in the v3.0.0 release notes).
6. The in-app updater finds the release by itself (it needs `IXC-Setup-vX.Y.Z.exe` and `SHA256SUMS.txt` in the release assets).

Unsigned installers show SmartScreen warnings. A code-signing certificate (or [SignPath Foundation](https://signpath.org) for
open source) removes them.