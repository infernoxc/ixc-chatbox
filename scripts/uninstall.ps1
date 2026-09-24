# Uninstaller for IXC Music / IXC ChatBox.
#   -App music|chat   remove only that app (the shared helper stays if the other app is still installed)
#   -KeepSettings     keep %LOCALAPPDATA%\IXC-OBS\config.json and your phone key
# Your OBS browser sources are not touched - delete them in OBS.   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([ValidateSet('music', 'chat', 'all')][string]$App = 'all', [switch]$KeepSettings)
$dest = Join-Path $env:LOCALAPPDATA 'IXC-OBS'; $appDir = Join-Path $dest 'app'
$title = @{ music = 'IXC Music'; chat = 'IXC ChatBox' }
$remove = if ($App -eq 'all') { @('music', 'chat') } else { @($App) }
Write-Host ('Uninstalling ' + (($remove | % { $title[$_] }) -join ' + ') + '...')
# stop running parts
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { ($_.CommandLine -like '*ixc-helper.ps1*' -or $_.CommandLine -like '*chat-relay.ps1*') -and $_.ProcessId -ne $PID -and $_.CommandLine -notlike '*Get-CimInstance*' } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue }
# phone access reminder (needs admin to undo)
$cfg = $null; if (Test-Path "$dest\config.json") { try { $cfg = Get-Content -Raw "$dest\config.json" | ConvertFrom-Json } catch {} }
if ($remove -contains 'chat' -and $cfg -and $cfg.chatRelay.phoneAccess) { Write-Host '  Phone access was ON: run scripts\enable-phone-access.ps1 -Disable as administrator to remove its firewall rule (or delete the rule "IXC ChatBox - phone chat" in Windows Defender Firewall).' -ForegroundColor Yellow }
# OBS docks
$docks = "$appDir\scripts\add-obs-docks.ps1"
foreach ($a in $remove) { if (Test-Path $docks) {
  if (Get-Process obs64 -EA SilentlyContinue) { Write-Host "  OBS is open: remove the '$($title[$a])' dock later in OBS > Docks > Custom Browser Docks." -ForegroundColor Yellow }
  else { & powershell -NoProfile -ExecutionPolicy Bypass -File $docks -App $a -Remove | Out-Null } } }
# app files + shortcuts
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'IXC for OBS'
foreach ($a in $remove) { if (Test-Path "$appDir\$a") { Remove-Item -LiteralPath "$appDir\$a" -Recurse -Force }; $l = "$menu\Uninstall $($title[$a]).lnk"; if (Test-Path $l) { Remove-Item -LiteralPath $l -Force } }
$left = @('music', 'chat') | ? { Test-Path "$appDir\$_" }
if ($left) {   # the other app is still installed: keep the helper running for it
  Write-Host ("  Kept the shared helper for " + (($left | % { $title[$_] }) -join ', ') + '.')
  & powershell -NoProfile -ExecutionPolicy Bypass -File "$appDir\start.ps1"
} else {
  Unregister-ScheduledTask -TaskName 'IXC for OBS' -Confirm:$false -EA SilentlyContinue
  $startup = "$([Environment]::GetFolderPath('Startup'))\IXC for OBS.lnk"; if (Test-Path $startup) { Remove-Item -LiteralPath $startup -Force }
  if (Test-Path $menu) { Remove-Item -LiteralPath $menu -Recurse -Force }
  if (Test-Path $appDir) { Remove-Item -LiteralPath $appDir -Recurse -Force }
  if ($KeepSettings) { Write-Host "  Kept your settings in $dest" } elseif (Test-Path $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
}
Write-Host 'Done. Remove the IXC browser sources from your OBS scenes if you added them.' -ForegroundColor Green
