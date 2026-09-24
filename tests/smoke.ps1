# Smoke tests for IXC Music / IXC ChatBox (the same file is used in both repos - it tests whatever apps are in src\).
# Runs on a clean Windows 10/11 with Windows PowerShell 5.1 - no extra tools needed.
#   powershell -ExecutionPolicy Bypass -File tests\smoke.ps1            offline checks (used by CI)
#   powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -Online    + YouTube search / neural TTS (needs internet)
# Copies the app to a temporary folder (like a fresh install) and uses test ports 18767/18768, so it never touches a
# real installation.   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([switch]$Online)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $MyInvocation.MyCommand.Path)
$hasMusic = Test-Path "$repo\src\music"; $hasChat = Test-Path "$repo\src\chat"
$script:fail = 0; $script:pass = 0
function Check($name, [scriptblock]$test) {
  try { $r = & $test; if ($r -eq $false) { throw 'returned false' }; Write-Host "  PASS  $name" -ForegroundColor Green; $script:pass++ }
  catch { Write-Host "  FAIL  $name : $($_.Exception.Message)" -ForegroundColor Red; $script:fail++ } }
function Status($url, $headers = @{}) { try { (Invoke-WebRequest -UseBasicParsing $url -Headers $headers).StatusCode } catch { [int]$_.Exception.Response.StatusCode } }

Write-Host '1) Static checks'
foreach ($f in Get-ChildItem $repo -Recurse -Filter *.ps1 | ? { $_.FullName -notmatch '\\dist\\' }) {
  Check "parses: $($f.FullName.Replace($repo + '\', ''))" { $e = $null; [Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$e) | Out-Null; if ($e.Count) { throw $e[0].Message } } }
if ($hasChat) { Check 'chat relay (C#) compiles' { Add-Type -Path "$repo\src\chat\relay\ChatRelay.cs" -ReferencedAssemblies System.Web.Extensions, System.Net.Http } }
Check 'config.example.json is valid JSON' { $null = Get-Content -Raw "$repo\config\config.example.json" | ConvertFrom-Json }
Check 'no secrets / personal paths in shipped files' {
  $bad = Get-ChildItem "$repo\src", "$repo\scripts", "$repo\config" -Recurse -File |
    Select-String -Pattern '[A-Z]:\\Users\\[A-Za-z]', 'Stream Assets', '"password"\s*:\s*"[^"]+"', 'live_[a-z0-9]{20,}', 'sk-[A-Za-z0-9]{20,}', 'ghp_[A-Za-z0-9]{20,}'
  if ($bad) { throw (($bad | Select -First 3 | % { "$($_.Filename):$($_.LineNumber)" }) -join ', ') } }

Write-Host '2) Fresh-install run (temp folder, test ports)'
$tmp = Join-Path $env:TEMP ('ixc-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $tmp | Out-Null
Copy-Item "$repo\src\*" $tmp -Recurse
$cfg = Get-Content -Raw "$repo\config\config.example.json" | ConvertFrom-Json; $cfg.helper.port = 18767
if ($hasChat) { $cfg.chatRelay.port = 18768; $cfg.streamerbot.websocketUrl = 'ws://127.0.0.1:1/'; $cfg.streamerbot.settingsPath = '' }
$cfgFile = Join-Path $tmp 'test-config.json'; [IO.File]::WriteAllText($cfgFile, ($cfg | ConvertTo-Json -Depth 10))
$procs = @(Start-Process powershell.exe -PassThru -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$tmp\helper\ixc-helper.ps1`" -ConfigPath `"$cfgFile`"")
if ($hasChat) { $procs += Start-Process powershell.exe -PassThru -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$tmp\chat\relay\chat-relay.ps1`" -ConfigPath `"$cfgFile`"" }
$HU = 'http://localhost:18767'; $RU = 'http://localhost:18768'
try {
  for ($i = 0; $i -lt 40; $i++) { try { Invoke-RestMethod "$HU/api/ping" -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }
  Check 'helper answers /api/ping' { (Invoke-RestMethod "$HU/api/ping").app -eq 'ixc-helper' }
  Check 'blocks path traversal' { (Status "$HU/$(if ($hasMusic) { 'music' } else { 'chat' })/..%2F..%2Fhelper%2Fixc-helper.ps1") -ge 400 }
  Check 'rejects API calls from other websites' { (Status "$HU/api/state" @{ Origin = 'https://evil.example' }) -eq 403 }
  if ($hasMusic) {
    Check 'serves /music/player.html' { (Invoke-WebRequest -UseBasicParsing "$HU/music/player.html").Content -match 'IXC Music' }
    Check 'serves /music/dock.html' { (Status "$HU/music/dock.html") -eq 200 }
    Check 'dock -> player command queue' { Invoke-RestMethod "$HU/api/cmd" -Method Post -Body '{"cmd":"volume","v":30}' | Out-Null; (Invoke-RestMethod "$HU/api/cmds?since=0").cmds[0].cmd -eq 'volume' }
    Check 'player state round trip' { Invoke-RestMethod "$HU/api/state" -Method Post -Body '{"playing":true,"title":"test"}' | Out-Null; (Invoke-RestMethod "$HU/api/state").title -eq 'test' }
  }
  if ($hasChat) {
    Check 'serves /chat/chat.html and /chat/tts.html' { (Invoke-WebRequest -UseBasicParsing "$HU/chat/chat.html").Content -match 'IXC ChatBox' -and (Status "$HU/chat/tts.html") -eq 200 }
    Check 'TTS queue accepts a message' { (Invoke-RestMethod "$HU/api/say" -Method Post -Body '{"text":"hello","voice":"us-female"}').n -ge 1 }
    for ($i = 0; $i -lt 40; $i++) { try { Invoke-RestMethod "$RU/api/ping" -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }
    Check 'chat relay answers /api/ping (no Streamer.bot = streamerbot:false)' { $p = Invoke-RestMethod "$RU/api/ping"; $p.ok -and -not $p.streamerbot }
    Check 'chat relay: phone access OFF by default' { (Invoke-RestMethod "$RU/api/phone").disabled }
    Check 'chat relay: events endpoint' { $null -ne (Invoke-RestMethod "$RU/api/events?since=-1").seq }
    Check 'chat relay serves the phone page' { (Invoke-WebRequest -UseBasicParsing "$RU/").Content -match 'IXC ChatBox' }
  }
  if ($Online) {
    Write-Host '3) Online checks'
    if ($hasMusic) { Check 'YouTube search returns results' { @(Invoke-RestMethod "$HU/api/search?q=NCS%20Alan%20Walker").Count -ge 1 } }
    if ($hasChat) { Check 'neural TTS returns MP3 audio' { $r = Invoke-WebRequest -UseBasicParsing "$HU/api/tts.mp3?n=1" -TimeoutSec 40; $r.Headers['Content-Type'] -eq 'audio/mpeg' -and $r.RawContentLength -gt 1000 } }
  }
} finally {
  foreach ($p in $procs) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
  Start-Sleep 1; Remove-Item -LiteralPath $tmp -Recurse -Force -EA SilentlyContinue
}
Write-Host ''; Write-Host "Result: $($script:pass) passed, $($script:fail) failed" -ForegroundColor $(if ($script:fail) { 'Red' } else { 'Green' })
exit $script:fail
