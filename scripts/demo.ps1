$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src/Shelf/bin/Release/net8.0/Shelf.dll'
if (!(Test-Path -LiteralPath $app -PathType Leaf)) { throw 'Build Shelf.sln in Release mode first.' }

$folder = Join-Path $root ('demo/run-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$state = Join-Path $folder 'library.json'
Copy-Item -LiteralPath (Join-Path $root 'data/sample.json') -Destination $state

function Run-Shelf([int]$Expected, [string[]]$Tokens) {
    Write-Host ('> ' + ($Tokens -join ' '))
    & dotnet $app --data $state @Tokens
    if ($LASTEXITCODE -ne $Expected) {
        throw "Unexpected exit code $LASTEXITCODE (expected $Expected)."
    }
}

Run-Shelf 0 @('sections')
Run-Shelf 0 @('overdue', '2026-11-20', 'all')
Run-Shelf 0 @('overdue', '2026-11-20', 'week')
Run-Shelf 1 @('checkout', 'B201', 'M01', '2026-11-12', 'exam')
Run-Shelf 0 @('checkout', 'B201', 'M01', '2026-11-12', 'campus')
Run-Shelf 0 @('renew', 'B201', 'M01', '2026-11-20')
Run-Shelf 0 @('reserve', 'B101', 'M03')
Run-Shelf 1 @('collect', 'B101', 'M03', '2026-11-20', 'campus')
Run-Shelf 0 @('return', 'B101', 'M01', '2026-11-20')
Run-Shelf 0 @('collect', 'B101', 'M03', '2026-11-20', 'campus')
Run-Shelf 0 @('retire', 'B301')
Run-Shelf 0 @('sections')
Write-Host "Demo state saved at $state"
