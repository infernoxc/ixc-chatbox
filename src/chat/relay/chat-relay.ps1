# IXC ChatBox relay launcher (reply box + suggestions + optional phone chat).
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([string]$ConfigPath)
$here = Split-Path $MyInvocation.MyCommand.Path; $chatDir = Split-Path $here
$dataDir = Join-Path $env:LOCALAPPDATA 'IXC-OBS'; New-Item -ItemType Directory -Force $dataDir | Out-Null
$cfg = $null
foreach ($c in @($ConfigPath, (Join-Path $dataDir 'config.json'), (Join-Path $chatDir '..\..\config\config.example.json'))) {
  if ($c -and (Test-Path $c)) { try { $cfg = Get-Content -Raw -Encoding UTF8 $c | ConvertFrom-Json; break } catch {} } }
function Cfg($path, $default) { $o = $cfg; foreach ($p in $path.Split('.')) { if ($null -eq $o -or -not $o.PSObject.Properties[$p]) { return $default }; $o = $o.$p }; if ($null -eq $o) { $default } else { $o } }
if (-not [bool](Cfg 'chatRelay.enabled' $true)) { exit }

# private phone key: random, stored only on this PC (delete the file to make a new one)
$keyFile = Join-Path $dataDir 'phone_key.txt'
if (-not (Test-Path $keyFile)) { $k = -join ((48..57) + (97..122) | Get-Random -Count 24 | % { [char]$_ }); Set-Content $keyFile $k }
$key = (Get-Content $keyFile -TotalCount 1).Trim()

# Streamer.bot password: from config, or read from Streamer.bot's own settings.json on this PC
$sbSettings = [string](Cfg 'streamerbot.settingsPath' 'auto')
if ($sbSettings -eq 'auto' -or -not $sbSettings) {
  $sbSettings = ''
  $running = Get-Process -Name 'Streamer.bot' -EA SilentlyContinue | Select -First 1
  if ($running -and $running.Path) { $cand = Join-Path (Split-Path $running.Path) 'data\settings.json'; if (Test-Path $cand) { $sbSettings = $cand } }
  if (-not $sbSettings) {
    foreach ($root in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('MyDocuments'), $env:USERPROFILE, (Join-Path $env:USERPROFILE 'Downloads'), $env:LOCALAPPDATA, $env:APPDATA, 'C:\', 'D:\', 'C:\Program Files')) {
      if (-not (Test-Path $root)) { continue }
      $hit = Get-ChildItem $root -Directory -Filter 'Streamer.bot*' -EA SilentlyContinue | % { Join-Path $_.FullName 'data\settings.json' } | ? { Test-Path $_ } | Select -First 1
      if ($hit) { $sbSettings = $hit; break } } } }

$extra = [string](Cfg 'chatRelay.extraCommandsFile' '')
$hidden = @(Cfg 'chatRelay.hiddenCommands' @()) | % { [string]$_ }
Add-Type -Path (Join-Path $here 'ChatRelay.cs') -ReferencedAssemblies System.Web.Extensions, System.Net.Http
try {
  [ChatRelay]::Run($chatDir, $key, [string](Cfg 'streamerbot.websocketUrl' 'ws://127.0.0.1:8080/'), [string](Cfg 'streamerbot.password' ''), $sbSettings, $extra,
                   [int](Cfg 'chatRelay.port' 8768), [bool](Cfg 'chatRelay.phoneAccess' $false), [string[]]$hidden)
} catch { Add-Content (Join-Path $dataDir 'chat-relay.log') "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $($_.Exception.Message)" }
