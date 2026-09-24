# Installer for IXC Music / IXC ChatBox (per user, NO admin rights needed).
# Both projects share one small background program, IXC Core; installing both simply adds each app next to it.
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
#   .\scripts\install.ps1                         install / update, start it, add it to Windows login
#   .\scripts\install.ps1 -AddObsDocks            also add this app's dock to OBS (close OBS first)
#   .\scripts\install.ps1 -ChannelNames "a,b"     (ChatBox) extra names of your own accounts - TTS never reads them
param([switch]$AddObsDocks, [string]$ChannelNames = '', [switch]$NoStart)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $MyInvocation.MyCommand.Path)
$apps = @('music', 'chat') | ? { Test-Path "$repo\src\$_" }
$title = @{ music = 'IXC Music'; chat = 'IXC ChatBox' }
$dest = Join-Path $env:LOCALAPPDATA 'IXC-OBS'; $app = Join-Path $dest 'app'
$version = if (Test-Path "$repo\VERSION") { (Get-Content "$repo\VERSION" -TotalCount 1).Trim() } else { '2.0.0' }
Write-Host ("Installing " + (($apps | % { $title[$_] }) -join ' + ') + " v$version to $dest") -ForegroundColor Red
if ($PSVersionTable.PSVersion.Major -lt 5) { throw 'Windows PowerShell 5.1 or newer is required (built into Windows 10/11).' }

# 1. stop running parts, copy files (the shared core is replaced by the newest copy)
if (Test-Path "$app\stop.ps1") { & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\stop.ps1" | Out-Null }
Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe'" | ? { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($app, [StringComparison]::OrdinalIgnoreCase) } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue }   # also when an older stop.ps1 missed it
foreach ($old in "$app\helper", "$app\chat\relay", "$app\scripts\enable-phone-access.ps1") { if (Test-Path $old) { Remove-Item -LiteralPath $old -Recurse -Force } }   # v1.x parts
New-Item -ItemType Directory -Force "$app\core", "$app\scripts" | Out-Null
Get-ChildItem "$app\core" -Exclude 'ixc-core.exe' -EA SilentlyContinue | Remove-Item -Recurse -Force
Copy-Item "$repo\src\core\*" "$app\core\" -Recurse -Force
$coreVer = if (Test-Path "$app\core\VERSION") { [version](Get-Content "$app\core\VERSION" -TotalCount 1).Trim() } else { [version]'0.0' }
if ([version]$version -ge $coreVer) { Set-Content "$app\core\VERSION" $version }
foreach ($a in $apps) { if (Test-Path "$app\$a") { Remove-Item -LiteralPath "$app\$a" -Recurse -Force }; New-Item -ItemType Directory -Force "$app\$a" | Out-Null; Copy-Item "$repo\src\$a\*" "$app\$a\" -Recurse -Force; Set-Content "$app\$a\VERSION" $version }
Copy-Item "$repo\src\start.ps1", "$repo\src\stop.ps1" $app -Force
Get-ChildItem "$repo\scripts" -Filter *.ps1 | ? Name -ne 'install.ps1' | Copy-Item -Destination "$app\scripts\" -Force
Get-ChildItem $app -Recurse -File | Unblock-File -EA SilentlyContinue   # files from a downloaded zip are marked "from the internet"

# 2. build IXC Core on this PC (C# compiler built into Windows; nothing is downloaded)
for ($i = 0; $i -lt 20 -and (Test-Path "$app\core\ixc-core.exe"); $i++) { try { Remove-Item -LiteralPath "$app\core\ixc-core.exe" -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 500 } }
if (Test-Path "$app\core\ixc-core.exe") { throw "IXC Core is still running and couldn't be replaced. Start menu > IXC for OBS > Stop IXC, then install again." }
& powershell -NoProfile -ExecutionPolicy Bypass -File "$app\core\build-core.ps1" -Out "$app\core\ixc-core.exe"
if ($LASTEXITCODE -or -not (Test-Path "$app\core\ixc-core.exe")) { throw 'IXC Core could not be built - see docs/TROUBLESHOOTING.md' }

# 3. settings: create config.json once, then only add what's missing (your changes are kept); v1 settings are moved to their v2 places
$cfgFile = Join-Path $dest 'config.json'; $example = Get-Content -Raw -Encoding UTF8 "$repo\config\config.example.json" | ConvertFrom-Json
Copy-Item "$repo\config\config.example.json" (Join-Path $dest "config.example.$(($apps -join '+')).json") -Force
function Merge($into, $from) { foreach ($p in $from.PSObject.Properties) {
    $have = $into.PSObject.Properties[$p.Name]
    if (-not $have) { $into | Add-Member -NotePropertyName $p.Name -NotePropertyValue $p.Value }
    elseif ($p.Value -is [pscustomobject] -and $have.Value -is [pscustomobject] -and $p.Name -ne 'voices') { Merge $have.Value $p.Value } } }
if (-not (Test-Path $cfgFile)) { $cfg = $example; Write-Host '  created config.json (your settings)' }
else {
  Copy-Item $cfgFile "$cfgFile.before-v$version.bak" -Force
  $cfg = Get-Content -Raw -Encoding UTF8 $cfgFile | ConvertFrom-Json
  if ($cfg.PSObject.Properties['chatRelay']) {   # v1 -> v2: the relay is part of IXC Core now
    if (-not $cfg.PSObject.Properties['chat']) { $cfg | Add-Member -NotePropertyName chat -NotePropertyValue ([pscustomobject]@{}) }
    foreach ($k in 'hiddenCommands', 'extraCommandsFile') { if ($cfg.chatRelay.PSObject.Properties[$k] -and -not $cfg.chat.PSObject.Properties[$k]) { $cfg.chat | Add-Member -NotePropertyName $k -NotePropertyValue $cfg.chatRelay.$k } }
    if ($cfg.chatRelay.phoneAccess) { Write-Host '  v1 phone access (Wi-Fi only) was ON. v2 no longer needs it: run "scripts\enable-phone-access.ps1 -Disable" from the v1 download as administrator, or delete the firewall rule "IXC ChatBox - phone chat".' -ForegroundColor Yellow }
    $cfg.PSObject.Properties.Remove('chatRelay') }
  if ($cfg.tts -and $cfg.tts.PSObject.Properties['enabled'] -and -not $cfg.tts.PSObject.Properties['on']) { $cfg.tts.PSObject.Properties.Remove('enabled') }   # v1 "enabled" meant "service available"; v2 "on" is the TTS switch
  Merge $cfg $example; $cfg.version = $version
  Write-Host "  kept your existing config.json (backup: config.json.before-v$version.bak)" }
if ($ChannelNames -and $cfg.PSObject.Properties['tts']) { $names = @($ChannelNames.Split(',') | % { $_.Trim() } | ? { $_ }); $cfg.tts | Add-Member -Force -NotePropertyName ownNames -NotePropertyValue $names }
[IO.File]::WriteAllText($cfgFile, ($cfg | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))

# 4. start at Windows login (Task Scheduler, current user only) + Start menu shortcuts
$act = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$app\start.ps1`""
$trg = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"; $trg.Delay = 'PT15S'
$set = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
try { Register-ScheduledTask -TaskName 'IXC for OBS' -Action $act -Trigger $trg -Settings $set -Principal (New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited) -Force | Out-Null; Write-Host '  starts automatically when you log in' }
catch { $sc = (New-Object -ComObject WScript.Shell).CreateShortcut("$([Environment]::GetFolderPath('Startup'))\IXC for OBS.lnk"); $sc.TargetPath = 'powershell.exe'; $sc.Arguments = $act.Arguments; $sc.WindowStyle = 7; $sc.Save(); Write-Host '  starts automatically when you log in (Startup folder)' }
$port = if ($cfg.helper.port) { [int]$cfg.helper.port } else { 8767 }
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'IXC for OBS'; New-Item -ItemType Directory -Force $menu | Out-Null
$ws = New-Object -ComObject WScript.Shell
function Link($name, $target, $arguments, $icon) { $s = $ws.CreateShortcut("$menu\$name.lnk"); $s.TargetPath = $target; $s.Arguments = $arguments; $s.WorkingDirectory = $app; if ($icon) { $s.IconLocation = $icon }; $s.WindowStyle = 7; $s.Save() }
Link 'Start IXC' 'powershell.exe' "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$app\start.ps1`"" 'imageres.dll,101'
Link 'Stop IXC' 'powershell.exe' "-NoProfile -ExecutionPolicy Bypass -File `"$app\stop.ps1`"" 'imageres.dll,100'
Link 'IXC settings (config.json)' 'notepad.exe' "`"$cfgFile`"" ''
$u = $ws.CreateShortcut("$menu\IXC diagnostics.url"); $u.TargetPath = "http://localhost:$port/diag"; $u.Save()
foreach ($a in $apps) { Link "Uninstall $($title[$a])" 'powershell.exe' "-NoProfile -ExecutionPolicy Bypass -File `"$app\scripts\uninstall.ps1`" -App $a" 'imageres.dll,89' }

# 5. start now, optional OBS dock
if (-not $NoStart) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\start.ps1"; for ($i = 0; $i -lt 20; $i++) { try { Invoke-RestMethod "http://localhost:$port/api/ping" -TimeoutSec 1 | Out-Null; break } catch { Start-Sleep -Milliseconds 400 } } }
if ($AddObsDocks) { foreach ($a in $apps) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\scripts\add-obs-docks.ps1" -App $a } }
$ok = try { (Invoke-RestMethod "http://localhost:$port/api/ping" -TimeoutSec 5).app -eq 'ixc-core' } catch { $false }
Write-Host ''
Write-Host ('  IXC Core running: ' + $(if ($NoStart) { 'not started (-NoStart)' } elseif ($ok) { 'YES' } else { "NO - is another program using port $port? See docs/TROUBLESHOOTING.md" }))
if ($apps -contains 'music') { Write-Host "  IXC Music    source: http://localhost:$port/music/player.html     dock: http://localhost:$port/music/dock.html" }
if ($apps -contains 'chat')  { Write-Host "  IXC ChatBox  dock:   http://localhost:$port/chat/chat.html?dock=1&viewers=1   TTS source: http://localhost:$port/chat/tts.html   overlay: http://localhost:$port/chat/chat.html?max=6&fade=45" }
Write-Host "  Diagnostics: http://localhost:$port/diag"
Write-Host '  Next: docs/INSTALL.md, step "Add it to OBS".' -ForegroundColor Yellow
