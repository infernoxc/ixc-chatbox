# Smoke tests for IXC Music / IXC ChatBox (the same file is used in both repos - it tests whatever apps are in src\).
# Runs on a clean Windows 10/11 with Windows PowerShell 5.1 - no extra tools needed.
#   powershell -ExecutionPolicy Bypass -File tests\smoke.ps1            offline checks (used by CI)
#   powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -Online    + YouTube / Spotify link checks, search, neural TTS (needs internet)
# Builds IXC Core into a temporary folder (like a fresh install) and runs it in test mode on ports 28767/28769, so it never
# touches a real installation, OBS or Streamer.bot.   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([switch]$Online)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $MyInvocation.MyCommand.Path)
$hasMusic = Test-Path "$repo\src\music"; $hasChat = Test-Path "$repo\src\chat"
$script:fail = 0; $script:pass = 0
function Check($name, [scriptblock]$test) {
  try { $r = & $test; if ($r -eq $false) { throw 'returned false' }; Write-Host "  PASS  $name" -ForegroundColor Green; $script:pass++ }
  catch { Write-Host "  FAIL  $name : $($_.Exception.Message)" -ForegroundColor Red; $script:fail++ } }
function Status($url, $headers = @{}, $method = 'GET', $body = $null) { try { $p = @{ UseBasicParsing = $true; Uri = $url; Headers = $headers; Method = $method }; if ($body) { $p.Body = $body }; (Invoke-WebRequest @p).StatusCode } catch { [int]$_.Exception.Response.StatusCode } }
# minimal WebSocket page for the tests
function WsOpen($url, $hello) { $w = New-Object Net.WebSockets.ClientWebSocket; $w.ConnectAsync([uri]$url, [Threading.CancellationToken]::None).Wait(5000) | Out-Null; if ($hello) { WsSend $w $hello }; $w }
function WsSend($w, $text) { $b = [Text.Encoding]::UTF8.GetBytes($text); $w.SendAsync([ArraySegment[byte]]$b, 'Text', $true, [Threading.CancellationToken]::None).Wait(5000) | Out-Null }
function WsWait($w, $type, $ms = 6000) {   # returns the first message of that type, or $null
  $buf = New-Object byte[] 262144; $until = (Get-Date).AddMilliseconds($ms)
  while ((Get-Date) -lt $until -and $w.State -eq 'Open') { $ms2 = New-Object IO.MemoryStream
    do { $t = $w.ReceiveAsync([ArraySegment[byte]]$buf, [Threading.CancellationToken]::None); if (-not $t.Wait([int][Math]::Max(1, ($until - (Get-Date)).TotalMilliseconds))) { return $null }; $ms2.Write($buf, 0, $t.Result.Count) } while (-not $t.Result.EndOfMessage)
    $m = [Text.Encoding]::UTF8.GetString($ms2.ToArray()) | ConvertFrom-Json; if ($m.type -eq $type) { return $m } }
  $null }

Write-Host '1) Static checks'
foreach ($f in Get-ChildItem $repo -Recurse -Filter *.ps1 | ? { $_.FullName -notmatch '\\dist\\' }) {
  Check "parses: $($f.FullName.Replace($repo + '\', ''))" { $e = $null; [Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$e) | Out-Null; if ($e.Count) { throw $e[0].Message } } }
Check 'config.example.json is valid JSON' { $null = Get-Content -Raw "$repo\config\config.example.json" | ConvertFrom-Json }
Check 'no secrets / personal paths in shipped files' {
  $bad = Get-ChildItem "$repo\src", "$repo\scripts", "$repo\config" -Recurse -File -Exclude *.exe |
    Select-String -Pattern '[A-Z]:\\Users\\[A-Za-z]', 'Stream Assets', '"password"\s*:\s*"[^"]+"', '"clientSecret"\s*:\s*"[^"]+"', 'live_[a-z0-9]{20,}', 'sk-[A-Za-z0-9]{20,}', 'ghp_[A-Za-z0-9]{20,}', 'trycloudflare\.com/m#p='
  if ($bad) { throw (($bad | Select -First 3 | % { "$($_.Filename):$($_.LineNumber)" }) -join ', ') } }

Write-Host '2) Fresh-install run (temp folder, test ports, test mode)'
$tmp = Join-Path $env:TEMP ('ixc-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $tmp | Out-Null
Copy-Item "$repo\src\*" $tmp -Recurse; Set-Content "$tmp\core\VERSION" (Get-Content "$repo\VERSION" -TotalCount 1)
Check 'IXC Core builds with the C# compiler that ships with Windows' { & powershell -NoProfile -ExecutionPolicy Bypass -File "$tmp\core\build-core.ps1" -Out "$tmp\core\ixc-core.exe" | Out-Null; Test-Path "$tmp\core\ixc-core.exe" }
$cfg = Get-Content -Raw "$repo\config\config.example.json" | ConvertFrom-Json; $cfg.helper.port = 28767; $cfg.remote.port = 28769; $cfg.remote.allowDownload = $false
if ($cfg.PSObject.Properties['obs']) { $cfg.obs.websocketUrl = 'ws://127.0.0.1:1/' }
if ($hasChat) { $cfg.streamerbot.websocketUrl = 'ws://127.0.0.1:1/'; $cfg.streamerbot.settingsPath = 'none'; $cfg.tts | Add-Member -Force ownNames @('MyChannel') }
$cfgFile = Join-Path $tmp 'test-config.json'; [IO.File]::WriteAllText($cfgFile, ($cfg | ConvertTo-Json -Depth 10))
$proc = Start-Process "$tmp\core\ixc-core.exe" -PassThru -WindowStyle Hidden -ArgumentList "--config `"$cfgFile`" --test"
$HU = 'http://localhost:28767'; $WU = 'ws://localhost:28767/ws'; $RU = 'http://127.0.0.1:28769'
function Api($path, $body) { if ($body) { Invoke-RestMethod "$HU$path" -Method Post -Body $body -ContentType 'application/json' } else { Invoke-RestMethod "$HU$path" } }
function Inject($user, $text, $id) { Api '/api/chat/inject' ('{"event":{"source":"Twitch","type":"ChatMessage"},"data":{"message":{"msgId":"' + $id + '","username":"' + $user.ToLower() + '","displayName":"' + $user + '","message":"' + $text + '"}}}') | Out-Null }
try {
  for ($i = 0; $i -lt 40; $i++) { try { Invoke-RestMethod "$HU/api/ping" -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Milliseconds 300 } }
  Check 'IXC Core answers /api/ping' { $p = Invoke-RestMethod "$HU/api/ping"; $p.app -eq 'ixc-core' -and $p.music -eq $hasMusic -and $p.chat -eq $hasChat }
  Check 'blocks path traversal' { (Status "$HU/core/..%2F..%2Ftest-config.json") -ge 400 -and (Status "$HU/core/../test-config.json") -ge 400 }
  Check 'rejects API calls from other websites' { (Status "$HU/api/diag" @{ Origin = 'https://evil.example' }) -eq 403 -and (Status "$HU/ws" @{ Origin = 'https://evil.example' }) -eq 403 }
  Check 'serves the shared pages (diagnostics, phone, script)' { (Status "$HU/diag") -eq 200 -and (Status "$HU/core/ixc.js") -eq 200 -and (Status "$HU/core/mobile.html") -eq 200 }
  Check 'diagnostics report CPU/RAM and parts' { $d = Invoke-RestMethod "$HU/api/diag"; $d.process.ramMB -gt 0 -and $d.remote.tunnel -eq 'off' }
  if ($hasMusic) {
    Check 'serves /music/player.html and /music/dock.html' { (Invoke-WebRequest -UseBasicParsing "$HU/music/player.html").Content -match 'IXC Music' -and (Status "$HU/music/dock.html") -eq 200 }
    Check 'dock -> player command is pushed instantly (WebSocket)' { $pl = WsOpen $WU '{"type":"hello","role":"player","topics":["music.cmd"]}'; $dk = WsOpen $WU '{"type":"hello","role":"musicdock","topics":["music.state"]}'
      Start-Sleep -Milliseconds 300; WsSend $dk '{"type":"music.cmd","c":{"cmd":"volume","v":30}}'; $m = WsWait $pl 'music.cmd'
      WsSend $pl '{"type":"music.state","s":{"t":1,"ready":true,"playing":true,"title":"smoke"}}'; $s = $null; for ($k = 0; $k -lt 3 -and -not ($s -and $s.s.title); $k++) { $s = WsWait $dk 'music.state' }
      $pl.Abort(); $dk.Abort(); $m.c.cmd -eq 'volume' -and $s.s.title -eq 'smoke' }
    Check 'v1 HTTP command API still works' { Api '/api/cmd' '{"cmd":"next"}' | Out-Null; (Api '/api/cmds?since=0').cmds[-1].cmd -eq 'next' }
    Check 'audio destination without OBS is saved and reported as pending' { $r = Api '/api/music/route' '{"mode":"kick"}'; $r.pending -and -not $r.ok }
    Check 'audio destination rejects unknown modes' { -not (Api '/api/music/route' '{"mode":"nope"}').ok }
    Check 'unsupported links are refused with a reason' { $a = Api "/api/music/resolve?url=$([uri]::EscapeDataString('https://soundcloud.com/x/y'))"; $b = Api "/api/music/resolve?url=$([uri]::EscapeDataString('https://open.spotify.com/artist/0gxyHStUsqpMadRV0Di1Qt'))"
      $c = Api "/api/music/resolve?url=$([uri]::EscapeDataString('https://www.youtube.com/watch?list=RDdQw4w9WgXcQ'))"; -not $a.ok -and $a.code -eq 'unsupported' -and $b.code -eq 'unsupported' -and $c.code -eq 'unsupported' }
  }
  if ($hasChat) {
    Check 'serves /chat/chat.html and /chat/tts.html' { (Invoke-WebRequest -UseBasicParsing "$HU/chat/chat.html").Content -match 'IXC ChatBox' -and (Status "$HU/chat/tts.html") -eq 200 }
    Check 'TTS settings: bad values rejected, numbers clamped' { (Status "$HU/api/tts/settings" @{} 'POST' '{"voice":"nope"}') -eq 400 -and (Status "$HU/api/tts/settings" @{} 'POST' '{"hack":1}') -eq 400 -and (Api '/api/tts/settings' '{"speed":999}').settings.speed -eq 100 }
    Check 'TTS off: chat is not read' { Api '/api/tts/settings' '{"enabled":false}' | Out-Null; Inject 'Viewer1' 'hello there' 'a1'; (Api '/api/tts/state').queue.Count -eq 0 }
    Check 'TTS filters: own account, bots, commands, never-speak, duplicates' { Api '/api/tts/settings' '{"enabled":true,"neverSpeak":["Troll"],"userCooldownSec":0}' | Out-Null
      Inject 'MyChannel' 'my own reply' 'b1'; Inject 'Nightbot' 'follow the rules' 'b2'; Inject 'Viewer2' '!discord' 'b3'; Inject 'Troll' 'hi' 'b4'; Inject 'Viewer3' 'good game everyone' 'b5'; Inject 'Viewer3' 'good game everyone' 'b5'
      $s = Api '/api/tts/state'; $why = @($s.skipped | % reason); $n = @($s.queue).Count + @($s.current | ? { $_ }).Count
      ($why -contains 'your own account') -and ($why -contains 'bot account') -and ($why -contains 'chat command') -and ($why -contains 'never-speak list') -and $n -eq 1 -and (Api '/api/diag').chat.duplicatesDropped -ge 1 }
    Check 'TTS text cleanup (links, spam, shouting)' { Api '/api/tts/clear' '{}' | Out-Null; Inject 'Viewer4' 'WOW THIS IS AMAZING check https://example.com/x lol lol lol lol' 'c1'
      $t = (Api '/api/tts/state').queue[0].text; $t -match 'link' -and $t -notmatch 'https' -and $t -cnotmatch 'AMAZING' -and ([regex]::Matches($t, 'lol')).Count -eq 2 }
    Check 'TTS queue: clear and skip' { Api '/api/tts/clear' '{}' | Out-Null; (Api '/api/tts/state').queue.Count -eq 0 }
    Check 'chat pages get history on connect' { $c = WsOpen $WU '{"type":"hello","role":"chat","topics":["chat"]}'; $h = WsWait $c 'chat.history'; $c.Abort(); @($h.events).Count -ge 1 }
    Check 'reply without Streamer.bot fails with a clear reason' { $r = Api '/api/chat/send' '{"platform":"twitch","message":"hi"}'; -not $r.results[0].ok -and $r.results[0].error -match 'not connected' }
  }
  Write-Host '3) Phone access security (no tunnel is started in tests)'
  Check 'phone listener refuses unknown host names' { $q = [Net.HttpWebRequest]::Create("$RU/m"); $q.Host = 'evil.example'; try { $q.GetResponse().Close(); $false } catch { [int]$_.Exception.InnerException.Response.StatusCode -eq 421 } }
  Check 'phone listener exposes only the phone page (no PC pages / API)' { (Status "$RU/music/dock.html") -ge 403 -and (Status "$RU/api/diag" @{ Origin = $RU }) -eq 404 -and (Status "$RU/m") -eq 200 }
  Check 'pairing needs the right Origin and a valid one-time code' { (Status "$RU/api/pair" @{ Origin = 'https://evil.example' } 'POST' '{"code":"x"}') -eq 403 -and (Status "$RU/api/pair" @{ Origin = $RU } 'POST' '{"code":"wrong"}') -eq 401 }
  Check 'phone WebSocket without a session is refused' { $w = New-Object Net.WebSockets.ClientWebSocket; $w.Options.SetRequestHeader('Origin', $RU); $w.ConnectAsync([uri]'ws://127.0.0.1:28769/ws', [Threading.CancellationToken]::None).Wait(5000) | Out-Null
    WsSend $w '{"session":"forged"}'; $a = WsWait $w 'auth'; $w.Abort(); $a -and -not $a.ok }
  Check 'tunnel stays off until you ask for a QR code' { (Api '/api/remote/status').tunnel -eq 'off' }
  if ($Online) {
    Write-Host '4) Online checks'
    if ($hasMusic) {
      Check 'YouTube search returns results' { @(Api '/api/search?q=NCS%20Alan%20Walker').Count -ge 1 }
      Check 'YouTube link is checked with oEmbed' { $r = Api "/api/music/resolve?url=$([uri]::EscapeDataString('https://youtu.be/dQw4w9WgXcQ'))"; $r.ok -and $r.kind -eq 'youtube-video' }
      Check 'deleted YouTube video is refused' { -not (Api "/api/music/resolve?url=$([uri]::EscapeDataString('https://www.youtube.com/watch?v=aaaaaaaaaaa'))").ok }
      Check 'Spotify song is matched to YouTube' { $r = Api "/api/music/resolve?url=$([uri]::EscapeDataString('https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT'))"; $r.ok -and $r.item.id.Length -eq 11 }
    }
    if ($hasChat) { Check 'neural TTS returns MP3 audio' { $n = (Api '/api/tts/say' '{"text":"smoke test","voice":"in-female-2"}').n; $r = Invoke-WebRequest -UseBasicParsing "$HU/api/tts/audio?n=$n" -TimeoutSec 40; $r.Headers['Content-Type'] -eq 'audio/mpeg' -and $r.RawContentLength -gt 1000 } }
  }
  Check 'IXC Core is still running after all tests' { -not $proc.HasExited }
} finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
  Start-Sleep 1; Remove-Item -LiteralPath $tmp -Recurse -Force -EA SilentlyContinue
}
Write-Host ''; Write-Host "Result: $($script:pass) passed, $($script:fail) failed" -ForegroundColor $(if ($script:fail) { 'Red' } else { 'Green' })
exit $script:fail
