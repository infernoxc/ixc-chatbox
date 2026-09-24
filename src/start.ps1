# Starts the IXC background parts (hidden): the shared IXC Helper, and the IXC ChatBox relay if ChatBox is installed.
# Safe to run again - anything already running is left alone.   Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([string]$ConfigPath)
$here = Split-Path $MyInvocation.MyCommand.Path
$cfgArg = if ($ConfigPath) { " -ConfigPath `"$ConfigPath`"" } else { '' }
function Running($script) { [bool](Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -like "*$script*" -and $_.ProcessId -ne $PID -and $_.CommandLine -notlike '*Get-CimInstance*' }) }
foreach ($s in @(@('ixc-helper.ps1', "$here\helper\ixc-helper.ps1"), @('chat-relay.ps1', "$here\chat\relay\chat-relay.ps1"))) {
  if ((Test-Path $s[1]) -and -not (Running $s[0])) { Start-Process powershell.exe -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$($s[1])`"$cfgArg" } }
