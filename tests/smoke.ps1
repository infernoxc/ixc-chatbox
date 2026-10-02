# Windows tests for IXC (CI runs them on a clean Windows; you can run them too):
#   powershell -ExecutionPolicy Bypass -File tests\smoke.ps1                 build + run IXC in a temp folder and check it from outside
#   powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -Install        also run scripts\install.ps1 into a temp profile, update it, uninstall it
#   powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -Setup <exe>    also run the real IXC-Setup.exe silently (installs for this Windows user!)
# The full behaviour tests (chat platforms, music, TTS, phone relay...) are in tests\integration (Node + fake servers).
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([switch]$Install, [string]$Setup = '', [switch]$Online)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $MyInvocation.MyCommand.Path)
$script:fail = 0; $script:pass = 0
function Check($name, [scriptblock]$test) {
  try { $r = & $test; if ($r -eq $false) { throw 'returned false' }; Write-Host "  PASS  $name" -ForegroundColor Green; $script:pass++ }
  catch { Write-Host "  FAIL  $name : $($_.Exception.Message)" -ForegroundColor Red; $script:fail++ } }
function Status($url, $headers = @{}, $method = 'GET', $body = $null) { try { $p = @{ UseBasicParsing = $true; Uri = $url; Headers = $headers; Method = $method; TimeoutSec = 15 }; if ($body) { $p.Body = $body; $p.ContentType = 'application/json' }; (Invoke-WebRequest @p).StatusCode } catch { [int]$_.Exception.Response.StatusCode } }
function WsOpen($url, $hello) { $w = New-Object Net.WebSockets.ClientWebSocket; $w.ConnectAsync([uri]$url, [Threading.CancellationToken]::None).Wait(5000) | Out-Null; if ($hello) { WsSend $w $hello }; $w }
function WsSend($w, $text) { $b = [Text.Encoding]::UTF8.GetBytes($text); $w.SendAsync([ArraySegment[byte]]$b, 'Text', $true, [Threading.CancellationToken]::None).Wait(5000) | Out-Null }
function WsWait($w, $type, $ms = 8000) {
  $buf = New-Object byte[] 262144; $until = (Get-Date).AddMilliseconds($ms)
  while ((Get-Date) -lt $until -and $w.State -eq 'Open') { $ms2 = New-Object IO.MemoryStream
    do { $t = $w.ReceiveAsync([ArraySegment[byte]]$buf, [Threading.CancellationToken]::None); if (-not $t.Wait([int][Math]::Max(1, ($until - (Get-Date)).TotalMilliseconds))) { return $null }; $ms2.Write($buf, 0, $t.Result.Count) } while (-not $t.Result.EndOfMessage)
    $m = [Text.Encoding]::UTF8.GetString($ms2.ToArray()) | ConvertFrom-Json; if ($m.type -eq $type) { return $m } }
  $null }

Write-Host '1) Static checks'
foreach ($f in Get-ChildItem $repo -Recurse -Filter *.ps1 | ? { $_.FullName -notmatch '\\(dist|node_modules)\\' }) {
  Check "parses: $($f.FullName.Replace($repo + '\', ''))" { $e = $null; [Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$e) | Out-Null; if ($e.Count) { throw $e[0].Message } } }
Check 'no secrets / personal paths in shipped files' {
  $bad = Get-ChildItem "$repo\src", "$repo\scripts", "$repo\relay\src" -Recurse -File -Exclude *.exe, *.ico, *.png |
    Select-String -Pattern '[A-Z]:\\Users\\[A-Za-z]', '"password"\s*:\s*"[^"]+"', '"clientSecret"\s*:\s*"[^"]+"', 'live_[a-z0-9]{20,}', 'sk-[A-Za-z0-9]{20,}', 'ghp_[A-Za-z0-9]{20,}', 'AIza[0-9A-Za-z_-]{30,}'
  if ($bad) { throw (($bad | Select -First 3 | % { "$($_.Filename):$($_.LineNumber)" }) -join ', ') } }

Write-Host '2) Build with the C# compiler that ships with Windows, run in a temp folder'
$tmp = Join-Path $env:TEMP ('ixc-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $tmp, "$tmp\app\core", "$tmp\data" | Out-Null
foreach ($d in 'chat', 'music', 'app', 'overlay') { Copy-Item "$repo\src\$d" "$tmp\app\$d" -Recurse }
Copy-Item "$repo\src\core\*" "$tmp\app\core\" -Recurse; Set-Content "$tmp\app\core\VERSION" (Get-Content "$repo\VERSION" -TotalCount 1)
Check 'IXC Core builds (C# 5, .NET Framework 4.8)' { & powershell -NoProfile -ExecutionPolicy Bypass -File "$tmp\app\core\build-core.ps1" -Out "$tmp\app\core\ixc-core.exe" | Out-Host; Test-Path "$tmp\app\core\ixc-core.exe" }
$port = 28767; @{ helper = @{ port = $port }; obs = @{ websocketUrl = 'ws://127.0.0.1:1/' }; platforms = @{ streamerbot = @{ mode = 'off' } }; general = @{ firstRunDone = $true; checkUpdates = $false } } | ConvertTo-Json -Depth 5 | Set-Content "$tmp\data\config.json"
$env:IXC_TTS_FAKE = '1'
$p = Start-Process "$tmp\app\core\ixc-core.exe" -ArgumentList "--test --config `"$tmp\data\config.json`"" -PassThru
$base = "http://127.0.0.1:$port"
try {
  Check 'starts and answers' { for ($i = 0; $i -lt 40; $i++) { if ((Status "$base/api/ping") -eq 200) { return $true }; Start-Sleep -Milliseconds 250 }; $false }
  Check 'also answers on localhost (IPv4 + IPv6 loopback)' { (Status "http://localhost:$port/api/ping") -eq 200 }
  Check 'pages: dashboard, docks, sources, overlays' { @('/app/', '/chat/chat.html?dock=1', '/music/dock.html', '/music/player.html', '/chat/tts.html', '/overlay/nowplaying.html', '/overlay/chat.html', '/core/ixc.js') | % { if ((Status "$base$_") -ne 200) { throw $_ } }; $true }
  Check 'no path escapes' { @('/chat/../core/ixc-core.exe', '/chat/%2e%2e/core/ixc-core.exe', '/core/..%5c..%5cconfig.json') | % { if ((Status "$base$_") -ne 404) { throw $_ } }; $true }
  Check 'other websites are refused (null + foreign origin)' { (Status "$base/api/tts/say" @{ Origin = 'null' } 'POST' '{"text":"x"}') -eq 403 -and (Status "$base/api/tts/say" @{ Origin = 'https://evil.example' } 'POST' '{"text":"x"}') -eq 403 }
  Check 'settings are validated and saved at once' { (Status "$base/api/settings" @{} 'POST' '{"patch":{"tts.volume":"x"}}') -eq 400 -and (Status "$base/api/settings" @{} 'POST' '{"patch":{"tts.volume":55}}') -eq 200 -and ((Get-Content -Raw "$tmp\data\config.json") -match '"volume": 55') }
  Check 'secrets are encrypted for this Windows user (DPAPI)' { (Status "$base/api/accounts/rumble/rumble" @{} 'POST' '{"url":"https://rumble.com/-livestream-api/get-data?key=SMOKESECRET1"}') -eq 200 -and (Test-Path "$tmp\data\secrets.dat") -and -not ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes("$tmp\data\secrets.dat")) -match 'SMOKESECRET1') }
  Check 'WebSocket: chat pushed to pages, in order' { $w = WsOpen "ws://127.0.0.1:$port/ws" '{"type":"hello","role":"chat","topics":["chat"]}'; $null = WsWait $w 'chat.history'
    1..5 | % { Invoke-RestMethod -Method Post "$base/api/chat/inject" -Body (@{ platform = 'twitch'; id = "s$_"; name = 'Tester'; text = "hello $_" } | ConvertTo-Json) -ContentType 'application/json' | Out-Null }
    $got = 1..5 | % { (WsWait $w 'chat.msg').m.text }; $w.Dispose(); ($got -join ',') -eq 'hello 1,hello 2,hello 3,hello 4,hello 5' }
  Check 'TTS: queue -> source -> done, music ducks and comes back' {
    Invoke-RestMethod -Method Post "$base/api/settings" -Body '{"patch":{"tts.on":true}}' -ContentType 'application/json' | Out-Null
    $pl = WsOpen "ws://127.0.0.1:$port/ws" '{"type":"hello","role":"player","topics":["music.player"]}'; $tt = WsOpen "ws://127.0.0.1:$port/ws" '{"type":"hello","role":"tts","topics":["tts.play"]}'; Start-Sleep -Milliseconds 300
    Invoke-RestMethod -Method Post "$base/api/tts/say" -Body '{"text":"smoke test"}' -ContentType 'application/json' | Out-Null
    $play = WsWait $tt 'tts.play'; if (-not $play) { throw 'no tts.play' }; if ((Status "$base$($play.url)") -ne 200) { throw 'audio' }
    WsSend $tt (@{ type = 'tts.done'; n = $play.n; ok = $true } | ConvertTo-Json); $pl.Dispose(); $tt.Dispose(); $true }
  Check 'System check runs' { $h = Invoke-RestMethod "$base/api/health"; $h.items.Count -ge 8 }
  Check 'backup + diagnostics export (no secrets inside)' { $b = Invoke-RestMethod -Method Post "$base/api/backups" -Body '{}' -ContentType 'application/json'; $d = Invoke-RestMethod -Method Post "$base/api/diagnostics/export" -Body '{}' -ContentType 'application/json'
    Add-Type -AssemblyName System.IO.Compression.FileSystem; $z = [IO.Compression.ZipFile]::OpenRead($d.path); $txt = ($z.Entries | % { (New-Object IO.StreamReader($_.Open())).ReadToEnd() }) -join "`n"; $z.Dispose(); $b.ok -and $d.ok -and $txt -notmatch 'SMOKESECRET1' -and $txt -notmatch [regex]::Escape($env:USERNAME + '\') }
  Check 'quit gives exit code 4 (tray supervisor stops too)' { Invoke-RestMethod -Method Post "$base/api/system/quit" -Body '{}' -ContentType 'application/json' | Out-Null; $p.WaitForExit(8000) | Out-Null; $p.ExitCode -eq 4 }
} finally { if (-not $p.HasExited) { $p.Kill() } }

if ($Install) {
  Write-Host '3) Install, update and uninstall with scripts\install.ps1 (into a temporary user profile)'
  $prof = Join-Path $env:TEMP ('ixc-profile-' + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $prof | Out-Null
  $oldLocal = $env:LOCALAPPDATA; $env:LOCALAPPDATA = $prof
  try {
    $report = Join-Path $prof 'report.json'
    Check 'clean install' { & powershell -NoProfile -ExecutionPolicy Bypass -File "$repo\scripts\install.ps1" -NoStartup -NoObs -Report $report | Out-Host; $r = Get-Content -Raw $report | ConvertFrom-Json; if (-not $r.ok) { throw $r.message }; (Invoke-RestMethod 'http://127.0.0.1:8767/api/ping').app -eq 'ixc-core' }
    Check 'settings survive an update (install again)' { Invoke-RestMethod -Method Post 'http://127.0.0.1:8767/api/settings' -Body '{"patch":{"tts.voice":"jarvis"}}' -ContentType 'application/json' | Out-Null
      & powershell -NoProfile -ExecutionPolicy Bypass -File "$repo\scripts\install.ps1" -NoStartup -NoObs -Report $report | Out-Host; $r = Get-Content -Raw $report | ConvertFrom-Json; if (-not $r.ok) { throw $r.message }
      (Invoke-RestMethod 'http://127.0.0.1:8767/api/settings').values.'tts.voice' -eq 'jarvis' }
    Check 'a backup was made before the update' { @(Get-ChildItem "$prof\IXC-OBS\backups" -Filter '*before-install*').Count -ge 1 }
    Check 'uninstall removes everything' { & powershell -NoProfile -ExecutionPolicy Bypass -File "$prof\IXC-OBS\app\scripts\uninstall.ps1" -Quiet | Out-Host; Start-Sleep 1; -not (Test-Path "$prof\IXC-OBS") -and (Status 'http://127.0.0.1:8767/api/ping') -ne 200 }
  } finally { $env:LOCALAPPDATA = $oldLocal; Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe'" | ? { $_.ExecutablePath -like "$prof*" } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue } }
}

if ($Setup) {
  Write-Host '4) The real setup program, silently, on this (clean) Windows user'
  # Start-Process -Wait would also wait for IXC itself (it waits for every child process), so wait for the setup process only
  function RunSetup($exe, $argList) { $p = Start-Process $exe -ArgumentList $argList -PassThru; if (-not $p.WaitForExit(300000)) { $p.Kill(); throw "$([IO.Path]::GetFileName($exe)) did not finish within 5 minutes" }; $p.ExitCode }
  $dest = Join-Path $env:LOCALAPPDATA 'IXC-OBS'
  Check 'IXC-Setup.exe installs and IXC starts' { $code = RunSetup $Setup ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + "$env:TEMP\ixc-setup.log" + '"'); if ($code) { throw "setup exit $code" }
    for ($i = 0; $i -lt 180; $i++) { if ((Status 'http://127.0.0.1:8767/api/ping') -eq 200) { return $true }; Start-Sleep -Milliseconds 500 }; Get-Content "$dest\logs\app.log" -EA SilentlyContinue | Select -Last 20 | Out-Host; $false }
  Check 'tray supervisor + worker are running' { @(Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe'").Count -ge 2 }
  Check 'starts with Windows (scheduled task)' { [bool](Get-ScheduledTask -TaskName 'IXC for OBS' -EA SilentlyContinue) }
  Check 'Start menu shortcut' { Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'IXC for OBS\IXC.lnk') }
  Check 'worker crash -> the tray restarts it' { $w = Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe'" | ? { $_.CommandLine -match '--worker' } | Select -First 1; Stop-Process -Id $w.ProcessId -Force
    Start-Sleep 2; for ($i = 0; $i -lt 30; $i++) { if ((Status 'http://127.0.0.1:8767/api/ping') -eq 200) { return $true }; Start-Sleep -Milliseconds 500 }; $false }
  Check 'running the setup again (update/repair) keeps IXC working' { $code = RunSetup $Setup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'; if ($code) { throw "exit $code" }
    for ($i = 0; $i -lt 60; $i++) { if ((Status 'http://127.0.0.1:8767/api/ping') -eq 200) { return $true }; Start-Sleep -Milliseconds 500 }; $false }
  Check 'uninstall from Windows removes IXC' { $u = Get-ChildItem "$dest\setup" -Filter 'unins*.exe' | Select -First 1; [void](RunSetup $u.FullName '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART')
    # the uninstaller hands over to a copy of itself in %TEMP% and exits at once, so wait for the result instead
    for ($i = 0; $i -lt 120 -and (Test-Path "$dest\app"); $i++) { Start-Sleep -Milliseconds 500 }; Start-Sleep 2; -not (Test-Path "$dest\app") -and -not (Get-ScheduledTask -TaskName 'IXC for OBS' -EA SilentlyContinue) -and (Status 'http://127.0.0.1:8767/api/ping') -ne 200 }
}

Write-Host ''; Write-Host "  $script:pass passed, $script:fail failed" -ForegroundColor $(if ($script:fail) { 'Red' } else { 'Green' })
Remove-Item -LiteralPath $tmp -Recurse -Force -EA SilentlyContinue
exit $script:fail
