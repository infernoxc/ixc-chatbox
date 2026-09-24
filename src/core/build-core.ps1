# Compiles IXC Core (ixc-core.exe) from the .cs files next to this script, using the C# compiler that ships with Windows (.NET Framework 4.8).
# Nothing is downloaded. Part of IXC - Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
param([string]$Out = (Join-Path $PSScriptRoot 'ixc-core.exe'))
$ErrorActionPreference = 'Stop'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'; if (-not (Test-Path "$fw\csc.exe")) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc = Join-Path $fw 'csc.exe'; if (-not (Test-Path $csc)) { throw '.NET Framework 4.x C# compiler not found (Windows 10/11 include it).' }
$speech = @((Join-Path $fw 'WPF\System.Speech.dll'), (Get-ChildItem "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Speech" -Recurse -Filter System.Speech.dll -EA SilentlyContinue | Select -First 1 -Expand FullName)) | ? { $_ -and (Test-Path $_) } | Select -First 1
$src = Get-ChildItem $PSScriptRoot -Filter *.cs | % FullName
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$tmp = "$Out.new"
$arguments = @('/nologo', '/optimize+', '/target:exe', '/platform:anycpu', "/out:$tmp", '/r:System.Web.Extensions.dll', '/r:System.Net.Http.dll', "/r:$speech") + $src
$o = & $csc @arguments; if ($LASTEXITCODE -ne 0) { $o | ? { $_ -match 'error' } | Select -First 30 | % { Write-Host $_ }; throw "IXC Core did not compile ($LASTEXITCODE)" }
$o | ? { $_ -match 'warning' } | Select -First 10 | % { Write-Host $_ }
Move-Item -Force $tmp $Out
Write-Host "built $Out ($([math]::Round((Get-Item $Out).Length / 1KB)) KB)"
