# IXC Suite installer (IXC Music + IXC ChatBox). Per user - NO admin rights needed. Used by IXC-Setup.exe and Install.bat.
#   detect system -> stop old IXC -> back up your settings -> install the new version next to the old one -> switch over ->
#   start IXC -> turn on OBS's WebSocket + add IXC's panels (when OBS is closed) -> health check -> roll back if IXC won't start.
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
#   .\scripts\install.ps1                 install / update / repair
#   .\scripts\install.ps1 -NoObs          don't touch OBS's settings          -NoStartup   don't start IXC with Windows
#   .\scripts\install.ps1 -NoStart        install only                         -Report <file>  write the result as JSON (for the setup program)
param([switch]$NoObs, [switch]$NoStartup, [switch]$NoStart, [switch]$Desktop, [string]$Report = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $MyInvocation.MyCommand.Path)
$dest = Join-Path $env:LOCALAPPDATA 'IXC-OBS'; $app = Join-Path $dest 'app'; $new = "$app.new"; $prev = "$app.prev"
$version = if (Test-Path "$repo\VERSION") { (Get-Content "$repo\VERSION" -TotalCount 1).Trim() } else { '0.0.0' }
$steps = New-Object System.Collections.ArrayList; $warnings = New-Object System.Collections.ArrayList
function Step($t) { [void]$steps.Add($t); Write-Host "  $t" }
function Warn($t) { [void]$warnings.Add($t); Write-Host "  ! $t" -ForegroundColor Yellow }
function Finish($ok, $msg) {
  if ($Report) { [IO.File]::WriteAllText($Report, (@{ ok = $ok; message = $msg; version = $version; steps = @($steps); warnings = @($warnings) } | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false)) }
  if ($ok) { Write-Host "`n  $msg" -ForegroundColor Green; exit 0 } else { Write-Host "`n  $msg" -ForegroundColor Red; exit 1 } }
trap { Finish $false ("Installation failed: " + $_.Exception.Message) }
Write-Host "Installing IXC $version into $dest" -ForegroundColor Red

# 1. this PC
if ($PSVersionTable.PSVersion.Major -lt 5) { throw 'Windows PowerShell 5.1 is required (built into Windows 10 and 11).' }
$net = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -EA SilentlyContinue).Release
if (-not $net -or $net -lt 528040) { throw '.NET Framework 4.8 is missing. Run Windows Update (it is part of Windows 10 version 1903 and newer, and Windows 11), then install again.' }
Step ("Windows " + [Environment]::OSVersion.Version + ", .NET Framework 4.8 ok")
$obsRunning = [bool](Get-Process obs64, obs -EA SilentlyContinue)
$obsUsed = Test-Path (Join-Path $env:APPDATA 'obs-studio')
Step ("OBS Studio: " + $(if ($obsRunning) { 'open' } elseif ($obsUsed) { 'found' } else { 'not found (install it from obsproject.com - IXC sets itself up when OBS is there)' }))
if (Get-Process 'Streamer.bot' -EA SilentlyContinue) { Step 'Streamer.bot: running (optional - IXC uses it when it is there)' }

# 2. stop the running IXC (it saves your settings and queue first) and older versions' helpers
New-Item -ItemType Directory -Force $dest | Out-Null
if (Test-Path "$app\stop.ps1") { & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\stop.ps1" | Out-Null }
Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe' OR Name='cloudflared.exe'" | ? { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($dest, [StringComparison]::OrdinalIgnoreCase) } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue }
Start-Sleep -Milliseconds 500

# 3. back up your settings, sign-ins, phones and queue
$bk = Join-Path $dest 'backups'; New-Item -ItemType Directory -Force $bk | Out-Null
$have = @('config.json', 'secrets.dat', 'music.json') | % { Join-Path $dest $_ } | ? { Test-Path $_ }
if ($have) { Add-Type -AssemblyName System.IO.Compression.FileSystem; $zip = Join-Path $bk ("{0}_before-install-{1}.zip" -f (Get-Date -f 'yyyy-MM-dd_HH-mm-ss'), $version)
  $z = [IO.Compression.ZipFile]::Open($zip, 'Create'); foreach ($f in $have) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f, (Split-Path $f -Leaf)) }; $z.Dispose(); Step "backed up your settings ($(Split-Path $zip -Leaf))" }

# 4. install the new files next to the old ones, then switch over (the old version stays until the new one starts)
if (Test-Path $new) { Remove-Item -LiteralPath $new -Recurse -Force }
New-Item -ItemType Directory -Force "$new\core", "$new\scripts" | Out-Null
foreach ($d in 'chat', 'music', 'app', 'overlay') { if (Test-Path "$repo\src\$d") { Copy-Item "$repo\src\$d" "$new\$d" -Recurse } }
Copy-Item "$repo\src\core\*" "$new\core\" -Recurse
Set-Content "$new\core\VERSION" $version
Copy-Item "$repo\src\start.ps1", "$repo\src\stop.ps1" $new
Get-ChildItem "$repo\scripts" -Filter *.ps1 | Copy-Item -Destination "$new\scripts\"
Get-ChildItem $new -Recurse -File | Unblock-File -EA SilentlyContinue   # files from a downloaded zip are marked "from the internet"
if (-not (Test-Path "$new\core\ixc-core.exe")) {   # installing from source: build it with the compiler that ships with Windows
  & powershell -NoProfile -ExecutionPolicy Bypass -File "$new\core\build-core.ps1" -Out "$new\core\ixc-core.exe" | Out-Null
  if ($LASTEXITCODE -or -not (Test-Path "$new\core\ixc-core.exe")) { throw 'IXC could not be built on this PC - see docs/TROUBLESHOOTING.md' } }
Get-ChildItem "$new\core" -Filter *.cs | Remove-Item -Force -EA SilentlyContinue   # sources are not needed to run
if (Test-Path $prev) { Remove-Item -LiteralPath $prev -Recurse -Force -EA SilentlyContinue }
if (Test-Path $app) { for ($i = 0; $i -lt 20; $i++) { try { Rename-Item -LiteralPath $app -NewName (Split-Path $prev -Leaf) -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds 500 } }; if (Test-Path $app) { throw 'the old IXC is still running and could not be replaced. Restart the PC and install again.' } }
Rename-Item -LiteralPath $new -NewName (Split-Path $app -Leaf)
foreach ($old in "$dest\bin") { if (Test-Path $old) { Remove-Item -LiteralPath $old -Recurse -Force -EA SilentlyContinue } }   # v2's downloaded cloudflared is no longer used
Step "installed IXC $version"

# 5. start with Windows (Task Scheduler, this user only; no PowerShell window) + Start menu
$exe = Join-Path $app 'core\ixc-core.exe'
Unregister-ScheduledTask -TaskName 'IXC for OBS' -Confirm:$false -EA SilentlyContinue
$startupLnk = "$([Environment]::GetFolderPath('Startup'))\IXC for OBS.lnk"; if (Test-Path $startupLnk) { Remove-Item -LiteralPath $startupLnk -Force }
$ws = New-Object -ComObject WScript.Shell
if (-not $NoStartup) {
  $act = New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path $exe)
  $trg = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"; $trg.Delay = 'PT10S'
  $set = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
  try { Register-ScheduledTask -TaskName 'IXC for OBS' -Action $act -Trigger $trg -Settings $set -Principal (New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited) -Force | Out-Null; Step 'starts with Windows' }
  catch { $sc = $ws.CreateShortcut($startupLnk); $sc.TargetPath = $exe; $sc.WorkingDirectory = (Split-Path $exe); $sc.Save(); Step 'starts with Windows (Startup folder)' } }
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'IXC for OBS'
if (Test-Path $menu) { Remove-Item -LiteralPath $menu -Recurse -Force }
New-Item -ItemType Directory -Force $menu | Out-Null
function Link($path, $target, $arguments, $icon) { $s = $ws.CreateShortcut($path); $s.TargetPath = $target; if ($arguments) { $s.Arguments = $arguments }; $s.WorkingDirectory = (Split-Path $exe); $s.IconLocation = $(if ($icon) { $icon } else { "$exe,0" }); $s.Save() }
Link "$menu\IXC.lnk" $exe '' ''
Link "$menu\Uninstall IXC.lnk" 'powershell.exe' "-NoProfile -ExecutionPolicy Bypass -File `"$app\scripts\uninstall.ps1`"" 'imageres.dll,89'
if ($Desktop) { Link "$([Environment]::GetFolderPath('Desktop'))\IXC.lnk" $exe '' '' }
Step 'Start menu: IXC'

# 6. start IXC and check that it answers; if it doesn't, go back to the previous version
$cfgFile = Join-Path $dest 'config.json'
$port = try { (Get-Content -Raw $cfgFile | ConvertFrom-Json).helper.port } catch { $null }; if (-not $port) { $port = 8767 }
function Ping { try { $r = Invoke-RestMethod "http://127.0.0.1:$port/api/ping" -TimeoutSec 2; return $r.app -eq 'ixc-core' } catch { return $false } }
if (-not $NoStart) {
  Start-Process $exe -WorkingDirectory (Split-Path $exe)
  $ok = $false; for ($i = 0; $i -lt 40 -and -not $ok; $i++) { Start-Sleep -Milliseconds 500; $ok = Ping; if (-not $ok) { try { $port = (Get-Content -Raw $cfgFile | ConvertFrom-Json).helper.port } catch { } } }
  if (-not $ok) {
    if (Test-Path $prev) {
      Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe'" | ? { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($dest, [StringComparison]::OrdinalIgnoreCase) } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue }
      Start-Sleep -Milliseconds 800; Rename-Item -LiteralPath $app -NewName 'app.failed'; Rename-Item -LiteralPath $prev -NewName (Split-Path $app -Leaf); Start-Process (Join-Path $app 'core\ixc-core.exe')
      Remove-Item -LiteralPath "$dest\app.failed" -Recurse -Force -EA SilentlyContinue
      throw "the new version didn't start, so the previous version was put back. Please send IXC > System check > Export diagnostics to support." }
    throw "IXC didn't start. See $dest\logs\app.log" }
  Step "IXC is running (http://localhost:$port)"
  # 7. OBS: turn on its WebSocket server and add IXC's panels - only while OBS is closed (OBS rewrites its settings when it exits)
  if (-not $NoObs) {
    if ($obsRunning) { Warn 'OBS is open: IXC finishes the OBS setup by itself - or close OBS and run IXC > System check.' }
    elseif (-not $obsUsed) { Warn 'Open OBS once after installing it - IXC then sets itself up (IXC > System check).' }
    else { foreach ($a in 'obs.websocket', 'obs.docks') { try { $r = Invoke-RestMethod -Method Post "http://127.0.0.1:$port/api/repair" -Body (@{ action = $a } | ConvertTo-Json) -ContentType 'application/json' -TimeoutSec 15; if ($r.ok) { Step $r.message } else { Warn $r.error } } catch { Warn "OBS step ${a}: $($_.Exception.Message)" } } } }
}
if (Test-Path $prev) { Remove-Item -LiteralPath $prev -Recurse -Force -EA SilentlyContinue }
Finish $true "IXC $version is installed. Open it from the tray (bottom right) or the Start menu."
