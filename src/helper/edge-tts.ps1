# Edge "Read aloud" neural TTS -> MP3 bytes, using a small hand-written TLS WebSocket client (needs full header control).
# Part of IXC ChatBox - Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
# NOTE: this talks to Microsoft's public, UNOFFICIAL "Read aloud" endpoint that Microsoft Edge uses. It is not a documented
#       API, is provided by Microsoft (not by this project), may stop working at any time, and its use is subject to
#       Microsoft's terms. The client token below is the public constant shipped inside Microsoft Edge (also used by the
#       open-source edge-tts project); it is not a secret and not tied to any user. See docs/THIRD-PARTY.md.
# Usage (dot-source first):  EdgeTts 'text' 'en-IN-PrabhatNeural' '+0%' '+0Hz'
$script:EdgeToken = '6A5AA1D4EAFF4E9FB37E23D68491D6F4'
$script:EdgeVer = '1-143.0.3650.75'
function WsSend($stream, [byte[]]$payload, [int]$opcode = 1) {
  $len = $payload.Length; $hdr = New-Object Collections.Generic.List[byte]; $hdr.Add([byte](0x80 -bor $opcode))
  if ($len -lt 126) { $hdr.Add([byte](0x80 -bor $len)) } elseif ($len -lt 65536) { $hdr.Add(0xFE); $hdr.Add([byte]($len -shr 8)); $hdr.Add([byte]($len -band 255)) }
  else { $hdr.Add(0xFF); for ($i = 7; $i -ge 0; $i--) { $hdr.Add([byte](([long]$len -shr (8 * $i)) -band 255)) } }
  $mask = New-Object byte[] 4; (New-Object Random).NextBytes($mask); $hdr.AddRange($mask)
  $body = New-Object byte[] $len; for ($i = 0; $i -lt $len; $i++) { $body[$i] = $payload[$i] -bxor $mask[$i % 4] }
  $all = $hdr.ToArray() + $body; $stream.Write($all, 0, $all.Length); $stream.Flush() }
function ReadN($stream, [int]$n) { $b = New-Object byte[] $n; $o = 0; while ($o -lt $n) { $r = $stream.Read($b, $o, $n - $o); if ($r -le 0) { throw 'connection closed' }; $o += $r }; $b }
function WsRecv($stream) {   # returns @(opcode, bytes) - handles fragmentation
  $data = New-Object IO.MemoryStream; $op = 0
  do { $h = ReadN $stream 2; $fin = ($h[0] -band 0x80) -ne 0; $o = $h[0] -band 0x0F; if ($o) { $op = $o }
    $len = [long]($h[1] -band 0x7F); if ($len -eq 126) { $e = ReadN $stream 2; $len = $e[0] * 256 + $e[1] } elseif ($len -eq 127) { $e = ReadN $stream 8; $len = 0; foreach ($x in $e) { $len = $len * 256 + $x } }
    if ($len) { $p = ReadN $stream ([int]$len); $data.Write($p, 0, $p.Length) } } until ($fin)
  , @($op, $data.ToArray()) }
function EdgeTts([string]$text, [string]$voice = 'en-IN-PrabhatNeural', [string]$rate = '+0%', [string]$pitch = '+0Hz') {
  $ticks = [long](([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + 11644473600) * 10000000); $ticks -= $ticks % 3000000000
  $gec = -join ([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::ASCII.GetBytes("$ticks$script:EdgeToken")) | % { $_.ToString('X2') })
  $cid = [guid]::NewGuid().ToString('N'); $hostName = 'speech.platform.bing.com'
  $path = "/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken=$script:EdgeToken&Sec-MS-GEC=$gec&Sec-MS-GEC-Version=$script:EdgeVer&ConnectionId=$cid"
  $tcp = New-Object Net.Sockets.TcpClient; $tcp.ReceiveTimeout = 15000; $tcp.SendTimeout = 15000; $tcp.Connect($hostName, 443)
  $ssl = New-Object Net.Security.SslStream $tcp.GetStream(), $false; $ssl.AuthenticateAsClient($hostName, $null, [Security.Authentication.SslProtocols]::Tls12, $false)
  try {
    $key = [Convert]::ToBase64String((1..16 | % { [byte](Get-Random -Max 256) }))
    $req = "GET $path HTTP/1.1`r`nHost: $hostName`r`nUpgrade: websocket`r`nConnection: Upgrade`r`nSec-WebSocket-Key: $key`r`nSec-WebSocket-Version: 13`r`n" +
           "Origin: chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold`r`nPragma: no-cache`r`nCache-Control: no-cache`r`nAccept-Encoding: gzip, deflate, br`r`nAccept-Language: en-US,en;q=0.9`r`n" +
           "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0`r`n`r`n"
    $rb = [Text.Encoding]::ASCII.GetBytes($req); $ssl.Write($rb, 0, $rb.Length); $ssl.Flush()
    $resp = New-Object Text.StringBuilder; while (-not $resp.ToString().EndsWith("`r`n`r`n")) { $c = $ssl.ReadByte(); if ($c -lt 0) { throw 'no handshake' }; [void]$resp.Append([char]$c) }
    if ($resp.ToString() -notmatch '^HTTP/1\.1 101') { throw ('TTS handshake refused: ' + $resp.ToString().Split("`r")[0]) }
    $ts = [DateTime]::UtcNow.ToString('ddd MMM dd yyyy HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture) + ' GMT+0000 (Coordinated Universal Time)'
    WsSend $ssl ([Text.Encoding]::UTF8.GetBytes("X-Timestamp:$ts`r`nContent-Type:application/json; charset=utf-8`r`nPath:speech.config`r`n`r`n" +
      '{"context":{"synthesis":{"audio":{"metadataoptions":{"sentenceBoundaryEnabled":"false","wordBoundaryEnabled":"false"},"outputFormat":"audio-24khz-48kbitrate-mono-mp3"}}}}'))
    $safe = [Security.SecurityElement]::Escape($text)
    WsSend $ssl ([Text.Encoding]::UTF8.GetBytes("X-RequestId:$cid`r`nContent-Type:application/ssml+xml`r`nX-Timestamp:${ts}Z`r`nPath:ssml`r`n`r`n" +
      "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><voice name='$voice'><prosody pitch='$pitch' rate='$rate' volume='+0%'>$safe</prosody></voice></speak>"))
    $audio = New-Object IO.MemoryStream; $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
      $m = WsRecv $ssl; $op = $m[0]; $b = $m[1]
      if ($op -eq 2 -and $b.Length -gt 2) { $hl = $b[0] * 256 + $b[1]; if ([Text.Encoding]::ASCII.GetString($b, 2, $hl) -match 'Path:audio') { $audio.Write($b, 2 + $hl, $b.Length - 2 - $hl) } }
      elseif ($op -eq 1 -and [Text.Encoding]::UTF8.GetString($b) -match 'Path:turn\.end') { break }
      elseif ($op -eq 8) { break } }
    , $audio.ToArray()
  } finally { $ssl.Dispose(); $tcp.Close() } }
