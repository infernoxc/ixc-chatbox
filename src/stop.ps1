# Stops the IXC background parts (helper + ChatBox relay).   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
$n = 0
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { ($_.CommandLine -like '*ixc-helper.ps1*' -or $_.CommandLine -like '*chat-relay.ps1*') -and $_.ProcessId -ne $PID -and $_.CommandLine -notlike '*Get-CimInstance*' } |
  % { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue; $n++ }
"Stopped $n IXC process(es)."
