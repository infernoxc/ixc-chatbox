# Stops IXC (tray + background program) of this installation, and leftovers of older IXC versions.   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
$here = Split-Path $MyInvocation.MyCommand.Path; $root = Split-Path $here; $n = 0
$mine = { param($p) $p.ExecutablePath -and $p.ExecutablePath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) }
# ask politely first (saves settings and the music queue), then make sure
try { $cfg = Get-Content -Raw (Join-Path $root 'config.json') | ConvertFrom-Json; $port = if ($cfg.helper.port) { $cfg.helper.port } else { 8767 }; Invoke-RestMethod -Method Post "http://127.0.0.1:$port/api/system/quit" -Body '{}' -ContentType 'application/json' -TimeoutSec 3 | Out-Null; Start-Sleep -Milliseconds 1500 } catch { }
Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe' OR Name='cloudflared.exe'" | ? { & $mine $_ } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue; $n++ }
for ($i = 0; $i -lt 20 -and (Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe'" | ? { & $mine $_ }); $i++) { Start-Sleep -Milliseconds 250 }
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.ProcessId -ne $PID -and $_.CommandLine -match [regex]::Escape($root) -and $_.CommandLine -match 'ixc-helper\.ps1|chat-relay\.ps1' } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue; $n++ }
"Stopped $n IXC process(es)."
