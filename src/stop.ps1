# Stops IXC Core (and with it the phone tunnel, if it was on).   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
$here = Split-Path $MyInvocation.MyCommand.Path; $exe = Join-Path $here 'core\ixc-core.exe'; $n = 0
Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe'" | ? { $_.ExecutablePath -eq $exe } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue; $n++ }   # WMI: works from 32-bit PowerShell too
for ($i = 0; $i -lt 20 -and (Get-CimInstance Win32_Process -Filter "Name='ixc-core.exe'" | ? { $_.ExecutablePath -eq $exe }); $i++) { Start-Sleep -Milliseconds 250 }
# v1.x helpers of this install, if any are still running
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.ProcessId -ne $PID -and $_.CommandLine -match [regex]::Escape($here) -and $_.CommandLine -match 'ixc-helper\.ps1|chat-relay\.ps1' } | % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue; $n++ }
"Stopped $n IXC process(es)."
