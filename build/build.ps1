# Builds the release files into .\dist\
#   powershell -ExecutionPolicy Bypass -File build\build.ps1            -> dist\IXC-ChatBox-v<VERSION>.zip + SHA256SUMS.txt
#   ... -Installer   also builds dist\IXC-ChatBox-Setup-v<VERSION>.exe (needs Inno Setup 6: winget install JRSoftware.InnoSetup)
# Copyright (c) 2026 Ishan (InFerNoxC) - MIT License
param([switch]$Installer, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $MyInvocation.MyCommand.Path); Set-Location $repo
$version = (Get-Content "$repo\VERSION" -TotalCount 1).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "VERSION '$version' is not SemVer" }
if (-not $SkipTests) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$repo\tests\smoke.ps1"; if ($LASTEXITCODE) { throw 'Smoke tests failed - not building.' } }

$name = "IXC-ChatBox-v$version"; $dist = Join-Path $repo 'dist'; $stage = Join-Path $dist $name
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
foreach ($item in 'src', 'scripts', 'config', 'docs') { Copy-Item "$repo\$item" "$stage\$item" -Recurse }
foreach ($file in 'Install.bat', 'Uninstall.bat', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CHANGELOG.md', 'SECURITY.md', 'VERSION') { Copy-Item "$repo\$file" $stage }
# never ship local settings / secrets
Get-ChildItem $stage -Recurse -File -Include 'config.json', 'phone_key.txt', '*.log', 'channel_names.txt' | Remove-Item -Force
$zip = Join-Path $dist "$name.zip"; if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# write entries with "/" separators (the ZIP standard) so every unzip tool extracts folders correctly
$fs = [IO.File]::Open($zip, 'Create'); $za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
foreach ($f in Get-ChildItem $stage -Recurse -File) {
  $entryName = $name + '/' + $f.FullName.Substring($stage.Length + 1).Replace('\', '/')
  $e = $za.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal); $e.LastWriteTime = $f.LastWriteTime
  $in = [IO.File]::OpenRead($f.FullName); $outS = $e.Open(); $in.CopyTo($outS); $outS.Dispose(); $in.Dispose() }
$za.Dispose(); $fs.Dispose()
$outputs = @($zip)

if ($Installer) {
  $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | ? { Test-Path $_ } | Select -First 1
  if (-not $iscc) { throw 'Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or build without -Installer.' }
  & $iscc "/DAppVersion=$version" "/DSourceDir=$stage" "/O$dist" "$repo\build\installer.iss" | Out-Host
  if ($LASTEXITCODE) { throw 'Inno Setup failed' }
  $outputs += Join-Path $dist "IXC-ChatBox-Setup-v$version.exe"
}
$sums = $outputs | % { "{0}  {1}" -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower(), (Split-Path $_ -Leaf) }
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), [string[]]$sums)
Remove-Item -LiteralPath $stage -Recurse -Force
Write-Host "Built:"; $outputs + (Join-Path $dist 'SHA256SUMS.txt') | % { "  $_  ({0:N0} KB)" -f ((Get-Item $_).Length / 1KB) }
