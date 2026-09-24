# OPTIONAL: lets your phone open IXC ChatBox over your home Wi-Fi. Run as administrator (right-click > Run with PowerShell as admin).
# What it changes (all reversible with -Disable):
#   1. an HTTP URL reservation so the chat relay may listen on your network (port 8768 by default)
#   2. a Windows Firewall rule that allows that port on PRIVATE (home) networks only
#   3. "phoneAccess": true in your config.json
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([switch]$Disable)
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')) { throw 'Please run this as administrator.' }
$dataDir = Join-Path $env:LOCALAPPDATA 'IXC-OBS'; $cfgFile = Join-Path $dataDir 'config.json'
$cfg = Get-Content -Raw -Encoding UTF8 $cfgFile | ConvertFrom-Json
$port = if ($cfg.chatRelay.port) { [int]$cfg.chatRelay.port } else { 8768 }
$user = "$env:USERDOMAIN\$env:USERNAME"; $rule = "IXC ChatBox - phone chat ($port)"
if ($Disable) {
  netsh http delete urlacl url=http://+:$port/ | Out-Null
  netsh advfirewall firewall delete rule name="$rule" | Out-Null
  $cfg.chatRelay.phoneAccess = $false; 'Phone access turned OFF.'
} else {
  netsh http add urlacl url=http://+:$port/ user="$user" | Out-Null
  netsh advfirewall firewall add rule name="$rule" dir=in action=allow protocol=TCP localport=$port profile=private | Out-Null
  $cfg.chatRelay.phoneAccess = $true
  $prof = (Get-NetConnectionProfile | Select -First 1).NetworkCategory
  'Phone access turned ON (private networks only).'
  if ($prof -ne 'Private') { "NOTE: your network is '$prof'. Set it to Private: Settings > Network & internet > (your connection) > Network profile type > Private." }
}
[IO.File]::WriteAllText($cfgFile, ($cfg | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
$app = Join-Path $dataDir 'app'; & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\stop.ps1" | Out-Null; & powershell -NoProfile -ExecutionPolicy Bypass -File "$app\start.ps1"
'Toolkit restarted. In the IXC ChatBox dock, press the phone button to get the QR code.'
