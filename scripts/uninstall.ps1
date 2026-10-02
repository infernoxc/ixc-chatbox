# IXC uninstaller: removes IXC, its OBS panels, its OBS scene/sources (when OBS is open), the Windows start entry and the Start menu.
#   -KeepSettings   keep your settings, sign-ins, phones and backups (%LOCALAPPDATA%\IXC-OBS) for a later reinstall
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([switch]$KeepSettings, [switch]$Quiet)
$dest = Join-Path $env:LOCALAPPDATA 'IXC-OBS'; $app = Join-Path $dest 'app'
Write-Host 'Uninstalling IXC...'
$port = try { (Get-Content -Raw "$dest\config.json" | ConvertFrom-Json).helper.port } catch { $null }; if (-not $port) { $port = 8767 }
# remove IXC's scene and sources from OBS while IXC and OBS can still talk
try { $r = Invoke-RestMethod -Method Post "http://127.0.0.1:$port/api/obs/remove" -Body '{}' -ContentType 'application/json' -TimeoutSec 10; if ($r.ok) { Write-Host "  removed from OBS: $($r.removed -join ', ')" } } catch { }
if (Test-Path "$app\stop.ps1") { & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\stop.ps1" | Out-Null }
Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe' OR Name='cloudflared.exe'" | ? { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($dest, [StringComparison]::OrdinalIgnoreCase) } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue }
# OBS panels
if (Test-Path "$app\scripts\add-obs-docks.ps1") {
  if (Get-Process obs64, obs -EA SilentlyContinue) { Write-Host '  OBS is open: remove the IXC panels later in OBS > Docks > Custom Browser Docks.' -ForegroundColor Yellow }
  else { & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\scripts\add-obs-docks.ps1" -Remove | Out-Null } }
Unregister-ScheduledTask -TaskName 'IXC for OBS' -Confirm:$false -EA SilentlyContinue
foreach ($l in "$([Environment]::GetFolderPath('Startup'))\IXC for OBS.lnk", "$([Environment]::GetFolderPath('Desktop'))\IXC.lnk") { if (Test-Path $l) { Remove-Item -LiteralPath $l -Force } }
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'IXC for OBS'; if (Test-Path $menu) { Remove-Item -LiteralPath $menu -Recurse -Force }
Start-Sleep -Milliseconds 500
foreach ($d in $app, "$app.prev", "$app.new", "$dest\bin", "$dest\updates") { if (Test-Path $d) { Remove-Item -LiteralPath $d -Recurse -Force -EA SilentlyContinue } }
if ($KeepSettings) { Write-Host "  Kept your settings in $dest" } elseif (Test-Path $dest) { Remove-Item -LiteralPath $dest -Recurse -Force -EA SilentlyContinue }
Write-Host 'IXC was removed.' -ForegroundColor Green
if (-not $Quiet) { Start-Sleep -Seconds 2 }
