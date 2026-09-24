# Starts IXC Core (hidden) - the one background program behind IXC Music and IXC ChatBox.
# Builds it first if needed (with the C# compiler that ships with Windows). Safe to run again: a running copy is left alone.
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([string]$ConfigPath)
$here = Split-Path $MyInvocation.MyCommand.Path; $core = Join-Path $here 'core'; $exe = Join-Path $core 'ixc-core.exe'
$log = Join-Path $env:LOCALAPPDATA 'IXC-OBS\ixc-core.log'
# find our core by its full path (WMI works from 32-bit and 64-bit PowerShell alike; Get-Process .Path does not)
function CoreProcs { Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe'" | ? { $_.ExecutablePath -eq $exe } }
# (re)build when the program is missing or older than its source (after an update)
$newest = Get-ChildItem $core -Filter *.cs -EA SilentlyContinue | Sort LastWriteTimeUtc -Descending | Select -First 1
if (-not (Test-Path $exe) -or ($newest -and (Get-Item $exe).LastWriteTimeUtc -lt $newest.LastWriteTimeUtc)) {
  CoreProcs | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue }; Start-Sleep -Milliseconds 700
  & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $core 'build-core.ps1') -Out $exe | Out-Null
  if ($LASTEXITCODE -or -not (Test-Path $exe)) { New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null; Add-Content $log "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') ERROR could not build IXC Core (see docs/TROUBLESHOOTING.md)"; exit 1 } }
if (CoreProcs) { exit 0 }
# v1.x helpers from an older IXC install use the same port: stop them (only this install's own copies, found by their path)
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.ProcessId -ne $PID -and $_.CommandLine -match [regex]::Escape($here) -and $_.CommandLine -match 'ixc-helper\.ps1|chat-relay\.ps1' } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue }
if ($ConfigPath) { Start-Process $exe -ArgumentList "--config `"$ConfigPath`"" -WindowStyle Hidden -WorkingDirectory $core }
else { Start-Process $exe -WindowStyle Hidden -WorkingDirectory $core }
