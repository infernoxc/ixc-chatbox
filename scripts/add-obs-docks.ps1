# Adds (or with -Remove, removes) the IXC panels in OBS Studio (Docks > Custom Browser Docks). OBS must be CLOSED.
# IXC normally does this by itself (System check > Add IXC panels); this script is used by the installer and uninstaller.
# Backs up the OBS settings file first.   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([switch]$Remove)
$ErrorActionPreference = 'Stop'
if (Get-Process obs64, obs -EA SilentlyContinue) { Write-Host 'Close OBS first (File > Exit), then run this again.' -ForegroundColor Yellow; exit 2 }
$obs = Join-Path $env:APPDATA 'obs-studio'
$ini = @("$obs\user.ini", "$obs\global.ini") | ? { (Test-Path $_) -and ([IO.File]::ReadAllText($_) -match '(?m)^\[BasicWindow\]') } | Select -First 1
if (-not $ini) { Write-Host 'OBS has not saved its window layout yet: open OBS once, close it, then run this again.' -ForegroundColor Yellow; exit 3 }
$dataDir = Join-Path $env:LOCALAPPDATA 'IXC-OBS'
$port = try { (Get-Content -Raw "$dataDir\config.json" | ConvertFrom-Json).helper.port } catch { $null }; if (-not $port) { $port = 8767 }
$mine = @('IXC', 'IXC ChatBox', 'IXC Music')
$want = @([pscustomobject]@{ title = 'IXC ChatBox'; url = "http://localhost:$port/chat/chat.html?dock=1&viewers=1" }, [pscustomobject]@{ title = 'IXC Music'; url = "http://localhost:$port/music/dock.html" }, [pscustomobject]@{ title = 'IXC'; url = "http://localhost:$port/app/?dock=1" })
Copy-Item $ini "$ini.before-ixc-$(Get-Date -f yyyyMMdd-HHmmss).bak"
$t = [IO.File]::ReadAllText($ini)
$line = [regex]::Match($t, '(?m)^ExtraBrowserDocks=(.*)$')
$docks = @(); if ($line.Success -and $line.Groups[1].Value.Trim()) { $docks = @($line.Groups[1].Value | ConvertFrom-Json) }
$docks = @($docks | ? { $mine -notcontains $_.title })
if (-not $Remove) { foreach ($w in $want) { $docks += [pscustomobject]@{ title = $w.title; url = $w.url; uuid = [guid]::NewGuid().ToString('N') } } }
$json = '[' + (($docks | % { ConvertTo-Json -InputObject $_ -Compress }) -join ', ') + ']'
if ($line.Success) { $t = $t.Remove($line.Index, $line.Length).Insert($line.Index, "ExtraBrowserDocks=$json") }
else { $t = $t -replace '(?m)^\[BasicWindow\]\r?$', "[BasicWindow]`r`nExtraBrowserDocks=$json" }
[IO.File]::WriteAllText($ini, $t, (New-Object Text.UTF8Encoding $false))
if ($Remove) { 'Removed the IXC panels from OBS.' } else { 'Added the IXC panels to OBS (Docks menu).' }
