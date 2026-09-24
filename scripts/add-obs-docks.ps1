# Adds (or with -Remove, removes) the IXC custom browser dock(s) in OBS Studio. OBS must be CLOSED.
# Backs up the OBS settings file first.   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([ValidateSet('music', 'chat')][string]$App = 'music', [string]$ChannelNames = '', [switch]$Remove)
$ErrorActionPreference = 'Stop'
if (Get-Process obs64 -EA SilentlyContinue) { Write-Host 'Close OBS first (File > Exit), then run this again.' -ForegroundColor Yellow; exit 1 }
$obs = Join-Path $env:APPDATA 'obs-studio'
$ini = @("$obs\user.ini", "$obs\global.ini") | ? { (Test-Path $_) -and ([IO.File]::ReadAllText($_) -match '(?m)^\[BasicWindow\]') } | Select -First 1
if (-not $ini) { Write-Host 'OBS settings not found. Start OBS once, close it, then run this again.' -ForegroundColor Yellow; exit 1 }
$dataDir = Join-Path $env:LOCALAPPDATA 'IXC-OBS'
$port = try { (Get-Content -Raw "$dataDir\config.json" | ConvertFrom-Json).helper.port } catch { $null }; if (-not $port) { $port = 8767 }
if (-not $ChannelNames -and (Test-Path "$dataDir\channel_names.txt")) { $ChannelNames = (Get-Content "$dataDir\channel_names.txt" -TotalCount 1).Trim() }
$own = if ($ChannelNames) { '&own=' + [uri]::EscapeDataString($ChannelNames) } else { '' }
$dock = @{ music = @{ title = 'IXC Music'; url = "http://localhost:$port/music/dock.html" }
           chat  = @{ title = 'IXC ChatBox'; url = "http://localhost:$port/chat/chat.html?dock=1&viewers=1&size=14&max=150&fade=0$own" } }[$App]
Copy-Item $ini "$ini.before-ixc-$(Get-Date -f yyyyMMdd-HHmmss).bak"
$t = [IO.File]::ReadAllText($ini)
$line = [regex]::Match($t, '(?m)^ExtraBrowserDocks=(.*)$')
$docks = @(); if ($line.Success -and $line.Groups[1].Value.Trim()) { $docks = @($line.Groups[1].Value | ConvertFrom-Json) }
$docks = @($docks | ? { $_.title -ne $dock.title })
if (-not $Remove) { $docks += [pscustomobject]@{ title = $dock.title; url = $dock.url; uuid = [guid]::NewGuid().ToString('N') } }
$json = '[' + (($docks | % { ConvertTo-Json -InputObject $_ -Compress }) -join ', ') + ']'
if ($line.Success) { $t = $t.Remove($line.Index, $line.Length).Insert($line.Index, "ExtraBrowserDocks=$json") }
else { $t = $t -replace '(?m)^\[BasicWindow\]\r?$', "[BasicWindow]`r`nExtraBrowserDocks=$json" }
[IO.File]::WriteAllText($ini, $t, (New-Object Text.UTF8Encoding $false))
if ($Remove) { "Removed the '$($dock.title)' dock from OBS." } else { "Added the '$($dock.title)' dock to OBS. Open OBS > Docks to show it." }
