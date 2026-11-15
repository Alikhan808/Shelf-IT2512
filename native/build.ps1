param([string]$Zig = 'zig')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'runtimes/win-x64/native'
New-Item -ItemType Directory -Force -Path $out | Out-Null

& $Zig cc -target x86_64-windows-gnu -std=c17 -O2 -Wall -Wextra -Werror -shared -s (Join-Path $PSScriptRoot 'shelf_fee.c') -o (Join-Path $out 'shelf_fee.dll')
if ($LASTEXITCODE -ne 0) { throw "Native build failed with exit code $LASTEXITCODE." }
