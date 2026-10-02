#!/bin/sh
# Builds IXC Core with Mono on Linux for the automated tests (Windows builds use src/core/build-core.ps1 and csc.exe).
# -langversion:5 keeps the code compatible with the C# 5 compiler that ships with Windows.
set -e
here=$(cd "$(dirname "$0")/.." && pwd); out=${1:-/tmp/ixc-test/app/core/ixc-core.exe}; stub=$(dirname "$out")/stub
mkdir -p "$(dirname "$out")" "$stub"
mcs -nologo -target:library -out:"$stub/System.Speech.dll" "$here/tests/stubs/speech.cs"
mcs -nologo -langversion:5 -optimize+ -out:"$out" -r:System.Web.Extensions.dll -r:System.Net.Http.dll -r:System.Security.dll -r:System.Drawing.dll \
  -r:System.Windows.Forms.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:"$stub/System.Speech.dll" "$here"/src/core/*.cs
cp "$stub/System.Speech.dll" "$(dirname "$out")/"
