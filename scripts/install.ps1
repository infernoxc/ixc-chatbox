# Installer for IXC Music / IXC ChatBox (per user, NO admin rights needed).
# Both projects share one small helper; installing both simply adds each app next to it.
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
#   .\scripts\install.ps1                         install / update, start it, add it to Windows login
#   .\scripts\install.ps1 -AddObsDocks            also add this app's dock to OBS (close OBS first)
#   .\scripts\install.ps1 -ChannelNames "a,b"     (ChatBox) your channel names - TTS skips your own bot replies
param([switch]$AddObsDocks, [string]$ChannelNames = '', [switch]$NoStart)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $MyInvocation.MyCommand.Path)
$apps = @('music', 'chat') | ? { Test-Path "$repo\src\$_" }
$title = @{ music = 'IXC Music'; chat = 'IXC ChatBox' }
$dest = Join-Path $env:LOCALAPPDATA 'IXC-OBS'; $app = Join-Path $dest 'app'
$version = if (Test-Path "$repo\VERSION") { (Get-Content "$repo\VERSION" -TotalCount 1).Trim() } else { '1.0.0' }
Write-Host ("Installing " + (($apps | % { $title[$_] }) -join ' + ') + " v$version to $dest") -ForegroundColor Red
if ($PSVersionTable.PSVersion.Major -lt 5) { throw 'Windows PowerShell 5.1 or newer is required (built into Windows 10/11).' }

# 1. stop running parts, copy files (the shared helper is replaced by the newest copy)
if (Test-Path "$app\stop.ps1") { & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\stop.ps1" | Out-Null }
New-Item -ItemType Directory -Force "$app\helper", "$app\scripts" | Out-Null
Copy-Item "$repo\src\helper\*" "$app\helper\" -Recurse -Force
foreach ($a in $apps) { New-Item -ItemType Directory -Force "$app\$a" | Out-Null; Copy-Item "$repo\src\$a\*" "$app\$a\" -Recurse -Force; Set-Content "$app\$a\VERSION" $version }
Copy-Item "$repo\src\start.ps1", "$repo\src\stop.ps1" $app -Force
Get-ChildItem "$repo\scripts" -Filter *.ps1 | ? Name -ne 'install.ps1' | Copy-Item -Destination "$app\scripts\" -Force
Get-ChildItem $app -Recurse -File | Unblock-File -EA SilentlyContinue   # files from a downloaded zip are marked "from the internet"

# 2. settings: create config.json once, then only add missing sections (your changes are kept)
$cfgFile = Join-Path $dest 'config.json'; $example = Get-Content -Raw -Encoding UTF8 "$repo\config\config.example.json" | ConvertFrom-Json
Copy-Item "$repo\config\config.example.json" (Join-Path $dest "config.example.$(($apps -join '+')).json") -Force
if (-not (Test-Path $cfgFile)) { $cfg = $example; Write-Host '  created config.json (your settings)' }
else { $cfg = Get-Content -Raw -Encoding UTF8 $cfgFile | ConvertFrom-Json
  foreach ($p in $example.PSObject.Properties) { if (-not $cfg.PSObject.Properties[$p.Name]) { $cfg | Add-Member -NotePropertyName $p.Name -NotePropertyValue $p.Value } }
  Write-Host '  kept your existing config.json' }
[IO.File]::WriteAllText($cfgFile, ($cfg | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
if ($ChannelNames) { Set-Content "$dest\channel_names.txt" $ChannelNames }

# 3. start at Windows login (Task Scheduler, current user only) + Start menu shortcuts
$act = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$app\start.ps1`""
$trg = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"; $trg.Delay = 'PT15S'
$set = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
try { Register-ScheduledTask -TaskName 'IXC for OBS' -Action $act -Trigger $trg -Settings $set -Principal (New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited) -Force | Out-Null; Write-Host '  starts automatically when you log in' }
catch { $sc = (New-Object -ComObject WScript.Shell).CreateShortcut("$([Environment]::GetFolderPath('Startup'))\IXC for OBS.lnk"); $sc.TargetPath = 'powershell.exe'; $sc.Arguments = $act.Arguments; $sc.WindowStyle = 7; $sc.Save(); Write-Host '  starts automatically when you log in (Startup folder)' }
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'IXC for OBS'; New-Item -ItemType Directory -Force $menu | Out-Null
$ws = New-Object -ComObject WScript.Shell
function Link($name, $target, $arguments, $icon) { $s = $ws.CreateShortcut("$menu\$name.lnk"); $s.TargetPath = $target; $s.Arguments = $arguments; $s.WorkingDirectory = $app; if ($icon) { $s.IconLocation = $icon }; $s.WindowStyle = 7; $s.Save() }
Link 'Start IXC' 'powershell.exe' "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$app\start.ps1`"" 'imageres.dll,101'
Link 'Stop IXC' 'powershell.exe' "-NoProfile -ExecutionPolicy Bypass -File `"$app\stop.ps1`"" 'imageres.dll,100'
Link 'IXC settings (config.json)' 'notepad.exe' "`"$cfgFile`"" ''
foreach ($a in $apps) { Link "Uninstall $($title[$a])" 'powershell.exe' "-NoProfile -ExecutionPolicy Bypass -File `"$app\scripts\uninstall.ps1`" -App $a" 'imageres.dll,89' }

# 4. start now, optional OBS dock
if (-not $NoStart) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\start.ps1"; Start-Sleep 4 }
if ($AddObsDocks) { foreach ($a in $apps) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\scripts\add-obs-docks.ps1" -App $a -ChannelNames $ChannelNames } }
$port = if ($cfg.helper.port) { [int]$cfg.helper.port } else { 8767 }
$ok = try { (Invoke-RestMethod "http://localhost:$port/api/ping" -TimeoutSec 5).app -eq 'ixc-helper' } catch { $false }
Write-Host ''
Write-Host ('  Helper running: ' + $(if ($NoStart) { 'not started (-NoStart)' } elseif ($ok) { 'YES' } else { "NO - is another program using port $port? See docs/TROUBLESHOOTING.md" }))
if ($apps -contains 'music') { Write-Host "  IXC Music    source: http://localhost:$port/music/player.html     dock: http://localhost:$port/music/dock.html" }
if ($apps -contains 'chat')  { Write-Host "  IXC ChatBox  dock:   http://localhost:$port/chat/chat.html?dock=1&viewers=1   TTS source: http://localhost:$port/chat/tts.html   overlay: http://localhost:$port/chat/chat.html?max=6&fade=45" }
Write-Host '  Next: docs/INSTALL.md, step "Add it to OBS".' -ForegroundColor Yellow
