# IXC Helper - tiny local web server shared by IXC Music and IXC ChatBox (https://github.com/infernoxc).
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License (see LICENSE)
#
# Listens on http://localhost:<port>/ only (never on your network).
#   /music/player.html  OBS browser source that plays YouTube audio     /music/dock.html  OBS custom dock (search, queue, controls)
#   /chat/chat.html     multi-platform chat (overlay + dock)             /chat/tts.html    OBS browser source that plays chat TTS
# API (used by those pages):
#   POST /api/cmd  (dock -> player)   GET /api/cmds?since=N (player polls)   GET|POST /api/state   GET /api/search?q=   GET /api/related?id=&exclude=
#   POST /api/say  GET /api/says?since=N   GET /api/tts.mp3?n=N   POST /api/skip   GET|POST /api/log   GET /api/version   GET /api/ping   GET /api/stats
param([string]$ConfigPath)
$ErrorActionPreference = 'Continue'
$here = Split-Path $MyInvocation.MyCommand.Path; $appRoot = Split-Path $here
$static = @{}; foreach ($d in 'music', 'chat') { if (Test-Path (Join-Path $appRoot $d)) { $static[$d] = Join-Path $appRoot $d } }   # whichever apps are installed

# ---------- configuration ----------
$cfg = $null
foreach ($c in @($ConfigPath, (Join-Path $env:LOCALAPPDATA 'IXC-OBS\config.json'), (Join-Path $appRoot '..\config\config.example.json'))) {
  if ($c -and (Test-Path $c)) { try { $cfg = Get-Content -Raw -Encoding UTF8 $c | ConvertFrom-Json; break } catch { Write-Warning "Bad config $c : $($_.Exception.Message)" } } }
function Cfg($path, $default) { $o = $cfg; foreach ($p in $path.Split('.')) { if ($null -eq $o -or -not $o.PSObject.Properties[$p]) { return $default }; $o = $o.$p }; if ($null -eq $o) { $default } else { $o } }
$port = [int](Cfg 'helper.port' 8767)
$dataDir = Join-Path $env:LOCALAPPDATA 'IXC-OBS'; New-Item -ItemType Directory -Force $dataDir | Out-Null
$logFile = Join-Path $dataDir 'helper.log'
function Log($m) { try { Add-Content -LiteralPath $logFile -Value ((Get-Date -f 'yyyy-MM-dd HH:mm:ss') + ' ' + $m) } catch {} }

$l = New-Object Net.HttpListener; $l.Prefixes.Add("http://localhost:$port/"); $l.Prefixes.Add("http://127.0.0.1:$port/")
try { $l.Start() } catch { Log "port $port busy (already running?) - exiting"; exit }
Log "IXC Helper started on port $port"

# ---------- TTS (Microsoft Edge "Read aloud" neural voices - unofficial, see docs/THIRD-PARTY.md) ----------
. (Join-Path $here 'edge-tts.ps1')
$VOICES = @{}
$defaultVoices = [ordered]@{ 'in-male' = @('en-IN-PrabhatNeural', '+0%', '+0Hz'); 'in-female' = @('en-IN-NeerjaNeural', '+0%', '+0Hz'); 'us-male' = @('en-US-AndrewNeural', '+0%', '+0Hz')
                             'us-female' = @('en-US-JennyNeural', '+0%', '+0Hz'); 'jarvis' = @('en-GB-RyanNeural', '+6%', '-3Hz') }
foreach ($k in $defaultVoices.Keys) { $VOICES[$k] = $defaultVoices[$k] }
$cv = Cfg 'tts.voices' $null; if ($cv) { foreach ($p in $cv.PSObject.Properties) { $VOICES[$p.Name] = @([string]$p.Value.voice, [string]$(if ($p.Value.rate) { $p.Value.rate } else { '+0%' }), [string]$(if ($p.Value.pitch) { $p.Value.pitch } else { '+0Hz' })) } }
$ttsEnabled = [bool](Cfg 'tts.enabled' $true); $ttsMax = [int](Cfg 'tts.maxChars' 240)
$says = New-Object 'Collections.Generic.List[object]'; $sayN = 0; $ttsErr = ''; $skipN = 0
function Synth($text, $vkey) { if (-not $ttsEnabled) { return $null }; $v = $VOICES[$vkey]; if (-not $v) { $v = $VOICES['in-male'] }
  for ($a = 0; $a -lt 2; $a++) { try { $b = EdgeTts $text $v[0] $v[1] $v[2]; if ($b.Length -gt 1000) { $script:ttsErr = ''; return ,([byte[]]$b) } } catch { $script:ttsErr = $_.Exception.Message } }
  $null }

# ---------- music state ----------
$cmds = New-Object 'Collections.Generic.List[object]'; $seq = 0; $state = '{}'
$stats = @{}; $started = Get-Date; $events = New-Object 'Collections.Generic.List[string]'
$UA = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0 Safari/537.36'; 'Accept-Language' = 'en-US,en;q=0.9' }
$types = @{ '.html' = 'text/html; charset=utf-8'; '.js' = 'text/javascript'; '.css' = 'text/css'; '.png' = 'image/png'; '.svg' = 'image/svg+xml'; '.ico' = 'image/x-icon'; '.json' = 'application/json' }

function Send($ctx, [int]$code, $body, $type = 'application/json; charset=utf-8') {
  $b = if ($body -is [byte[]]) { $body } elseif ($body -is [object[]]) { [byte[]]$body } else { [Text.Encoding]::UTF8.GetBytes([string]$body) }
  $r = $ctx.Response; $r.StatusCode = $code; $r.ContentType = $type; $r.Headers['Cache-Control'] = 'no-store'
  $origin = $ctx.Request.Headers['Origin']; if ($origin) { $r.Headers['Access-Control-Allow-Origin'] = $origin }
  try { $r.OutputStream.Write($b, 0, $b.Length) } catch {}; $r.Close() }
function Body($ctx) { $sr = New-Object IO.StreamReader $ctx.Request.InputStream, ([Text.Encoding]::UTF8); $t = $sr.ReadToEnd(); $sr.Close(); $t }
# only pages from this PC may use the API (OBS docks/sources, file:// pages); blocks random websites in your browser
function OriginOk($ctx) { $o = $ctx.Request.Headers['Origin']; if (-not $o -or $o -eq 'null') { return $true }
  return $o -match '^https?://(localhost|127\.0\.0\.1)(:\d+)?$' }
function Json($o) { ConvertTo-Json -InputObject $o -Compress }

# ---------- YouTube search / related (no API key; public web pages - may break if YouTube changes) ----------
function Search($q) {
  $u = 'https://www.youtube.com/results?search_query=' + [uri]::EscapeDataString($q) + '&sp=EgIQAQ%253D%253D'   # videos only
  $h = (Invoke-WebRequest -UseBasicParsing $u -Headers $UA -TimeoutSec 15).Content
  $out = New-Object 'Collections.Generic.List[object]'; $seen = @{}
  foreach ($m in [regex]::Matches($h, '"videoRenderer":\{"videoId":"([\w-]{11})"(.{0,4000}?)"title":\{"runs":\[\{"text":"((?:[^"\\]|\\.)*)"')) {
    $id = $m.Groups[1].Value; if ($seen[$id]) { continue }; $seen[$id] = 1
    $rest = $h.Substring($m.Index, [math]::Min(6000, $h.Length - $m.Index))
    $len = [regex]::Match($rest, '"lengthText":\{"accessibility":\{"accessibilityData":\{"label":"[^"]*"\}\},"simpleText":"([^"]+)"').Groups[1].Value
    $ch = [regex]::Match($rest, '"ownerText":\{"runs":\[\{"text":"((?:[^"\\]|\\.)*)"').Groups[1].Value
    $out.Add([pscustomobject]@{ id = $id; title = [regex]::Unescape($m.Groups[3].Value); length = $len; channel = [regex]::Unescape($ch) }); if ($out.Count -ge 15) { break } }
  if ($out.Count -eq 0) { return '[]' }; '[' + (($out | % { Json $_ }) -join ',') + ']' }
function Related($id, $exclude) {   # "up next" songs for autoplay: single songs only (no mixes / live / playlists, 1-12 min)
  if ($id -notmatch '^[\w-]{11}$') { return '[]' }
  $h = (Invoke-WebRequest -UseBasicParsing "https://www.youtube.com/watch?v=$id" -Headers $UA -TimeoutSec 15).Content
  $i = $h.IndexOf('"secondaryResults"'); if ($i -lt 0) { return '[]' }
  $sec = $h.Substring($i, [math]::Min(600000, $h.Length - $i)); $skip = @{}; foreach ($x in ($exclude -split ',')) { if ($x) { $skip[$x] = 1 } }; $skip[$id] = 1
  $out = New-Object 'Collections.Generic.List[object]'
  foreach ($chunk in ($sec -split '"lockupViewModel":\{') | Select -Skip 1) {
    $cid = [regex]::Match($chunk, '"contentId":"([^"]+)"').Groups[1].Value; $ctype = [regex]::Match($chunk, '"contentType":"([^"]+)"').Groups[1].Value
    if ($cid.Length -ne 11 -or $ctype -notmatch 'VIDEO' -or $skip[$cid]) { continue }
    $title = [regex]::Match($chunk, '"title":\{"content":"((?:[^"\\]|\\.)*)"').Groups[1].Value; if (-not $title) { continue }
    $len = [regex]::Match($chunk.Substring(0, [math]::Min(4000, $chunk.Length)), '"text":"(\d{1,2}:\d{2}(?::\d{2})?)"').Groups[1].Value; if (-not $len) { continue }
    $s2 = 0; foreach ($p2 in $len.Split(':')) { $s2 = $s2 * 60 + [int]$p2 }; if ($s2 -gt 720 -or $s2 -lt 60) { continue }
    $tt = [regex]::Unescape($title); if ($tt -match '(?i)gameplay|trailer|review|reaction|tutorial|podcast|\bnews\b|vlog|highlights|unboxing|#shorts|walkthrough') { continue }
    $score = if ($tt -match '(?i)music|song|lyric|official|audio|\bncs\b|feat|remix|lofi|\bft\.|\bmix\b|beats|\(.*\)| - ') { 1 } else { 0 }
    $skip[$cid] = 1; $out.Add([pscustomobject]@{ id = $cid; title = $tt; length = $len; s = $score }); if ($out.Count -ge 12) { break } }
  $best = @(@($out | ? { $_.s -eq 1 }) + @($out | ? { $_.s -eq 0 }) | Select -First 8)
  if ($best.Count -eq 0) { return '[]' }; '[' + (($best | % { Json ([pscustomobject]@{ id = $_.id; title = $_.title; length = $_.length }) }) -join ',') + ']' }

function StaticFile($ctx, $p) {
  $rel = [Uri]::UnescapeDataString($p.TrimStart('/'))
  if ($rel -eq '') { $ctx.Response.Redirect($(if ($static.music) { '/music/dock.html' } else { '/chat/chat.html?dock=1&demo=1' })); $ctx.Response.Close(); return }
  $alias = @{ 'player.html' = 'music/player.html'; 'dock.html' = 'music/dock.html'; 'tts.html' = 'chat/tts.html' }   # v0 URLs keep working
  if ($alias[$rel]) { $rel = $alias[$rel] }
  $parts = $rel.Split('/', 2); if ($parts.Count -lt 2 -or -not $static[$parts[0]]) { Send $ctx 404 'not found' 'text/plain'; return }
  $base = [IO.Path]::GetFullPath($static[$parts[0]]); $f = [IO.Path]::GetFullPath((Join-Path $base $parts[1]))
  if ($f.StartsWith($base, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $f -PathType Leaf) -and $types[[IO.Path]::GetExtension($f).ToLower()]) {
    Send $ctx 200 ([IO.File]::ReadAllBytes($f)) $types[[IO.Path]::GetExtension($f).ToLower()] } else { Send $ctx 404 'not found' 'text/plain' } }

# ---------- request loop ----------
while ($l.IsListening) {
  try { $ctx = $l.GetContext() } catch { break }
  $p = $ctx.Request.Url.AbsolutePath; $m = $ctx.Request.HttpMethod; $qs = $ctx.Request.QueryString
  $stats[$p] = 1 + [int]$stats[$p]
  try {
    if ($m -eq 'OPTIONS') { $ctx.Response.Headers['Access-Control-Allow-Methods'] = 'GET, POST'; $ctx.Response.Headers['Access-Control-Allow-Headers'] = 'Content-Type'; Send $ctx 204 '' }
    elseif ($p.StartsWith('/api/') -and -not (OriginOk $ctx)) { Send $ctx 403 '{"error":"origin not allowed"}' }
    elseif ($p -eq '/api/cmd' -and $m -eq 'POST') { $seq++; $cmds.Add([pscustomobject]@{ n = $seq; c = (Body $ctx) }); if ($cmds.Count -gt 200) { $cmds.RemoveRange(0, 100) }; Send $ctx 200 "{`"n`":$seq}" }
    elseif ($p -eq '/api/cmds') { $since = [int]("0" + $qs['since']); $new = @($cmds | ? { $_.n -gt $since } | % { $_.c }); Send $ctx 200 ("{`"seq`":$seq,`"cmds`":[" + ($new -join ',') + "]}") }
    elseif ($p -eq '/api/state' -and $m -eq 'POST') { $state = Body $ctx; Send $ctx 200 '{}' }
    elseif ($p -eq '/api/state') { Send $ctx 200 $state }
    elseif ($p -eq '/api/search') { Send $ctx 200 (Search $qs['q']) }
    elseif ($p -eq '/api/related') { Send $ctx 200 (Related $qs['id'] ([string]$qs['exclude'])) }
    elseif ($p -eq '/api/ping') { Send $ctx 200 ('{"ok":true,"app":"ixc-helper","version":' + (Json ([string](Cfg 'version' '1.0.0'))) + '}') }
    elseif ($p -eq '/api/say' -and $m -eq 'POST') { $o = (Body $ctx) | ConvertFrom-Json; $t = ([string]$o.text).Trim(); if ($t.Length -gt $ttsMax) { $t = $t.Substring(0, $ttsMax) }
      if ($t) { $sayN++; $says.Add([pscustomobject]@{ n = $sayN; text = $t; voice = [string]$o.voice; mp3 = $null }); if ($says.Count -gt 60) { $says.RemoveRange(0, 30) } }
      Send $ctx 200 "{`"n`":$sayN}" }
    elseif ($p -eq '/api/says') { $since = [int]("0" + $qs['since'])
      $new = @($says | ? { $_.n -gt $since } | % { "{`"n`":$($_.n)}" }); Send $ctx 200 ("{`"seq`":$sayN,`"skip`":$skipN,`"err`":" + (Json ([string]$ttsErr)) + ",`"items`":[" + ($new -join ',') + "]}") }
    elseif ($p -eq '/api/tts.mp3') { $n = [int]("0" + $qs['n']); $it = $says | ? n -eq $n | Select -First 1
      if (-not $it) { Send $ctx 404 'no such message' 'text/plain' } else { if (-not $it.mp3) { $it.mp3 = Synth $it.text $it.voice }
        if ($it.mp3) { Send $ctx 200 $it.mp3 'audio/mpeg' } else { Send $ctx 502 'tts failed' 'text/plain' } } }
    elseif ($p -eq '/api/skip' -and $m -eq 'POST') { $skipN++; Send $ctx 200 "{`"skip`":$skipN}" }
    elseif ($p -eq '/api/log' -and $m -eq 'POST') { $events.Add((Get-Date -f 'HH:mm:ss.fff') + ' ' + (Body $ctx)); if ($events.Count -gt 400) { $events.RemoveRange(0, 200) }; Send $ctx 200 '{}' }
    elseif ($p -eq '/api/log') { Send $ctx 200 ($events -join "`n") 'text/plain; charset=utf-8' }
    elseif ($p -eq '/api/version') { $v = [string]((Get-ChildItem -LiteralPath @($static.Values) -Filter *.html | % { $_.LastWriteTimeUtc.Ticks } | Sort -Descending | Select -First 1)); Send $ctx 200 "{`"v`":`"$v`"}" }
    elseif ($p -eq '/api/stats') { $o = [ordered]@{ upSeconds = [int]((Get-Date) - $started).TotalSeconds; ttsMessages = $sayN; ttsError = $ttsErr }; foreach ($k in $stats.Keys) { $o[$k] = $stats[$k] }; Send $ctx 200 (ConvertTo-Json $o -Compress) }
    else { StaticFile $ctx $p }
  } catch { Log "error $p : $($_.Exception.Message)"; try { Send $ctx 500 ('{"error":' + (Json ([string]$_.Exception.Message)) + '}') } catch {} }
}
