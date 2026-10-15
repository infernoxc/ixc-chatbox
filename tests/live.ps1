# Live checks against the REAL platforms (needs internet; results depend on who is live, so CI only reports them):
#   Twitch chat (anonymous), YouTube live chat on a 24/7 stream, Kick channel lookup + chat socket, the neural voice, YouTube search.
#   powershell -ExecutionPolicy Bypass -File tests\live.ps1 [-Twitch somechannel] [-YouTube @handle] [-Kick channel]
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([string]$Twitch = 'twitch', [string]$YouTube = '@LofiGirl', [string]$Kick = 'xqc')
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $MyInvocation.MyCommand.Path)
$tmp = Join-Path $env:TEMP ('ixc-live-' + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory "$tmp\app\core", "$tmp\data" | Out-Null
foreach ($d in 'chat', 'music', 'app', 'overlay') { Copy-Item "$repo\src\$d" "$tmp\app\$d" -Recurse }
Copy-Item "$repo\src\core\*" "$tmp\app\core\" -Recurse; Set-Content "$tmp\app\core\VERSION" 'live-test'
& powershell -NoProfile -ExecutionPolicy Bypass -File "$tmp\app\core\build-core.ps1" -Out "$tmp\app\core\ixc-core.exe" | Out-Null
$port = 28777
@{ helper = @{ port = $port }; obs = @{ websocketUrl = 'ws://127.0.0.1:1/' }; general = @{ firstRunDone = $true }; viewers = @{ refreshSec = 15 }
   platforms = @{ twitch = @{ enabled = $true; channel = $Twitch }; youtube = @{ enabled = $true; channel = $YouTube }; kick = @{ enabled = $true; channel = $Kick }; streamerbot = @{ mode = 'off' } } } | ConvertTo-Json -Depth 5 | Set-Content "$tmp\data\config.json"
$p = Start-Process "$tmp\app\core\ixc-core.exe" -ArgumentList "--test --config `"$tmp\data\config.json`"" -PassThru
$base = "http://127.0.0.1:$port"; $results = [ordered]@{}
try {
  for ($i = 0; $i -lt 40; $i++) { try { Invoke-RestMethod "$base/api/ping" | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }
  $until = (Get-Date).AddSeconds(90)
  while ((Get-Date) -lt $until) { $st = (Invoke-RestMethod "$base/api/chat/status").platforms; if ($st.twitch.state -eq 'connected' -and $st.youtube.state -in 'connected', 'unavailable' -and $st.kick.state -ne 'connecting') { break }; Start-Sleep 3 }
  $st = (Invoke-RestMethod "$base/api/chat/status").platforms
  foreach ($k in 'twitch', 'youtube', 'kick') { $results["$k chat"] = "$($st.$k.state) $($st.$k.detail)" }
  Start-Sleep 30
  $d = Invoke-RestMethod "$base/api/diag"; $results['messages received'] = ($d.chat.perPlatform | ConvertTo-Json -Compress)
  $v = Invoke-RestMethod "$base/api/viewers"; foreach ($k in 'twitch', 'youtube', 'kick') { $x = $v.platforms.$k; $results["$k viewers"] = "$($x.state) $($x.count) $($x.note)" }
  $h = Invoke-RestMethod "$base/api/health"; foreach ($i in $h.items | ? { $_.id -in 'tts', 'network' }) { $results[$i.name] = "$($i.level): $($i.message)" }
  try { $s = Invoke-RestMethod "$base/api/music/search?q=lofi%20hip%20hop"; $results['YouTube search'] = "$(@($s).Count) results" } catch { $results['YouTube search'] = "failed: $($_.Exception.Message)" }
  # what YouTube returns for the live page, fetched the way IXC fetches it (helps tell "not live" from a page IXC can't read)
  try { $yp = '@' + $YouTube.TrimStart('@')
    $r = Invoke-WebRequest -UseBasicParsing "https://www.youtube.com/$yp/live" -UserAgent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36' -Headers @{ Cookie = 'SOCS=CAI; CONSENT=YES+1'; 'Accept-Language' = 'en-US,en;q=0.9' } -MaximumRedirection 5
    $b = $r.Content; $marks = @{ canonicalWatch = $b -match '<link rel="canonical" href="https://www\.youtube\.com/watch\?v='; canonicalAny = ([regex]::Match($b, '<link rel="canonical" href="([^"]+)"')).Groups[1].Value; isLiveNow = $b -match '"isLiveNow":true'; isLive = $b -match '"isLive":true'; consent = $b -match 'consent\.youtube'; botCheck = $b -match "not a bot|unusual traffic"
      ogUrl = $b -match '<meta property="og:url" content="https://www\.youtube\.com/watch\?v='; videoDetails = $b -match '"videoDetails":\{"videoId":"'; shortlink = $b -match '<link rel="shortlinkUrl" href="https://youtu\.be/'
      playability = ([regex]::Match($b, '"playabilityStatus":\{"status":"([A-Z_]+)"')).Groups[1].Value }
    $results['YouTube page probe'] = "HTTP $($r.StatusCode), $($b.Length) chars, " + (($marks.GetEnumerator() | Sort Name | % { "$($_.Name)=$($_.Value)" }) -join ', ') }
  catch { $results['YouTube page probe'] = "failed: $($_.Exception.Message)" }
} finally { if (-not $p.HasExited) { $p.Kill() } }
Write-Host 'Live platform results:'; $results.GetEnumerator() | % { Write-Host ("  {0,-20} {1}" -f $_.Key, $_.Value) }
Get-Content "$tmp\data\logs\chat.log" -EA SilentlyContinue | Select -Last 15 | % { Write-Host "  log: $_" }
# Kick chat probe: what kick.com's own web app uses to connect to chat (Pusher key / cluster / websocket host), printed for a human
try {
  $ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36'
  $kh = (Invoke-WebRequest -UseBasicParsing 'https://kick.com/' -UserAgent $ua -TimeoutSec 20).Content
  $srcs = [regex]::Matches($kh, '<script[^>]+src="([^"]+\.js[^"]*)"') | % { $_.Groups[1].Value } | Select -Unique
  Write-Host "  kick probe: home $($kh.Length) chars, $(@($srcs).Count) scripts"
  $hits = 0
  foreach ($s in @('inline') + @($srcs)) {
    $t = if ($s -eq 'inline') { $kh } else { $u = if ($s -match '^https?:') { $s } elseif ($s -match '^//') { "https:$s" } else { "https://kick.com$s" }; try { (Invoke-WebRequest -UseBasicParsing $u -UserAgent $ua -TimeoutSec 20).Content } catch { '' } }
    if ($t -match 'accepted_providers|/v1/realtime|viewer/v1') { foreach ($m in [regex]::Matches($t, '(/v1/realtime[^"`'']*|viewer/v1[^"`'']*|accepted_providers|webFetch=|subscribe\(|centrifuge|Centrifuge\()')) { if ($script:rt -ge 60) { break }; $a = [Math]::Max(0, $m.Index - 400); Write-Host "  kick realtime [$([IO.Path]::GetFileName(($s -split '\?')[0]))]: $(($t.Substring($a, [Math]::Min(900, $t.Length - $a))) -replace '\s+', ' ')"; $script:rt++ } }
    foreach ($m in [regex]::Matches($t, '(?i)(pusher|wss://|ws-[a-z0-9]+\.|cluster|websocket|centrifug|ably|soketi|reverb)')) {
      if ($hits -ge 40) { break }; $a = [Math]::Max(0, $m.Index - 90); $snip = $t.Substring($a, [Math]::Min(220, $t.Length - $a)) -replace '\s+', ' '
      Write-Host "  kick probe [$([IO.Path]::GetFileName(($s -split '\?')[0]))]: $snip"; $hits++ } }
  if (-not $hits) { Write-Host '  kick probe: no chat connection settings found in kick.com files' } }
catch { Write-Host "  kick probe failed: $($_.Exception.Message)" }
