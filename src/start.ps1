# Starts IXC (tray icon + background program). Builds IXC Core first when running from source without a ready-made ixc-core.exe.
# Safe to run again: a running copy is left alone.   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
$here = Split-Path $MyInvocation.MyCommand.Path; $core = Join-Path $here 'core'; $exe = Join-Path $core 'ixc-core.exe'
if (-not (Test-Path $exe)) { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $core 'build-core.ps1') -Out $exe | Out-Null; if ($LASTEXITCODE -or -not (Test-Path $exe)) { Write-Host 'IXC could not be built - see docs/TROUBLESHOOTING.md'; exit 1 } }
Start-Process $exe -WorkingDirectory $core
