param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$files = @(
    'src/PBM.Web/src/BudgetReservations.tsx',
    'src/PBM.Web/src/BudgetTransfers.tsx',
    'src/PBM.Web/src/IdempotencyAdmin.tsx',
    'src/PBM.Web/src/ReservationReconciliationAdmin.tsx'
)

$old = "e.target.value === '' ? '' : Number(e.target.value)"
$new = "String(e.target.value) === '' ? '' : Number(e.target.value)"
$utf8 = New-Object System.Text.UTF8Encoding($false)
$total = 0

foreach ($relativePath in $files) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "File not found: $path"
    }

    $content = [System.IO.File]::ReadAllText($path)
    $count = ([regex]::Matches($content, [regex]::Escape($old))).Count
    if ($count -ne 1) {
        throw "Expected exactly one Select conversion in $relativePath but found $count. No further files were changed after this failure point."
    }

    $updated = $content.Replace($old, $new)
    [System.IO.File]::WriteAllText($path, $updated, $utf8)
    $total += $count
    Write-Host "$relativePath : fixed $count" -ForegroundColor Cyan
}

if ($total -ne 4) {
    throw "Expected 4 total fixes but applied $total."
}

Write-Host ''
Write-Host 'FRONTEND SELECT TYPE FIXES APPLIED: 4' -ForegroundColor Green
Write-Host 'Next: run .\scripts\resolve-production-blocker.ps1 -Action verify'
