# Releasing

**Versioning:** [SemVer](https://semver.org) `MAJOR.MINOR.PATCH`. The version is in `VERSION`, each release gets a tag `vX.Y.Z`,
a `CHANGELOG.md` section and `docs/release-notes/vX.Y.Z.md`.

1. Update `VERSION`, `CHANGELOG.md` and the release notes.
2. Test and build locally:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -Online
   powershell -ExecutionPolicy Bypass -File build\build.ps1 -Installer   # needs Inno Setup 6 (winget install JRSoftware.InnoSetup)
   ```
3. Commit, tag and push:
   ```powershell
   git commit -am "Release vX.Y.Z"; git tag -a vX.Y.Z -m "vX.Y.Z"; git push; git push origin vX.Y.Z
   ```
4. The **Release** workflow tests and builds on a clean Windows runner, then publishes the GitHub Release with the ZIP, the Setup
   `.exe` and `SHA256SUMS.txt`. To do it by hand: `gh release create vX.Y.Z dist\* --title "IXC ChatBox vX.Y.Z" --notes-file docs\release-notes\vX.Y.Z.md`
5. Download the release on a clean PC (or Windows Sandbox) and follow docs/INSTALL.md.

Unsigned installers show SmartScreen warnings. A code-signing certificate (or [SignPath Foundation](https://signpath.org) for
open source) removes them.