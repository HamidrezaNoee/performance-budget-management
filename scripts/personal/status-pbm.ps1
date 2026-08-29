. (Join-Path $PSScriptRoot 'common.ps1')

$root = Get-PbmRepoRoot
$migrationDir = Join-Path $root 'src/PBM.Infrastructure/Migrations'
$snapshot = Get-ChildItem -Path $migrationDir -Filter '*ModelSnapshot.cs' -File -ErrorAction SilentlyContinue | Select-Object -First 1
$backupDir = Get-PbmBackupDir
$backups = @(
    if (Test-Path $backupDir) {
        Get-ChildItem $backupDir -Filter '*.bak' -File | Sort-Object LastWriteTimeUtc -Descending
    }
)

$fontDir = Join-Path $root 'src/PBM.Web/public/fonts'
$requiredFonts = @(
    'iranyekanwebregular.woff',
    'iranyekanwebmedium.woff',
    'iranyekanwebbold.woff'
)
$missingFonts = @($requiredFonts | Where-Object { -not (Test-Path (Join-Path $fontDir $_)) })

$ready = $false
$readyDetail = 'not checked'
try {
    $response = Invoke-WebRequest -Uri "$(Get-PbmWebUrl)/readyz" -UseBasicParsing -TimeoutSec 5
    $ready = $response.StatusCode -eq 200
    $readyDetail = $response.Content
}
catch { $readyDetail = $_.Exception.Message }

Write-Host 'PBM Personal Deployment Status' -ForegroundColor Cyan
Write-Host "Git commit       : $(Get-PbmCurrentGitRef)"
Write-Host "EF migrations    : $(if ($snapshot) { 'available' } else { 'MISSING - real-data installation blocked' })"
Write-Host "UI fonts         : $(if ($missingFonts.Count -eq 0) { 'IranYekan READY' } else { 'MISSING: ' + ($missingFonts -join ', ') })"
Write-Host "Web URL          : $(Get-PbmWebUrl)"
Write-Host "Readiness        : $(if ($ready) { 'READY' } else { 'NOT READY' })"
Write-Host "Backup directory : $backupDir"
Write-Host "Backup count     : $($backups.Count)"
if ($backups.Count -gt 0) {
    Write-Host "Latest backup    : $($backups[0].FullName)"
    Write-Host "Latest backup UTC: $($backups[0].LastWriteTimeUtc.ToString('o'))"
}
Write-Host "Readiness detail : $readyDetail"

if (Test-Path (Join-Path $root '.pbm/install-state.json')) {
    Write-Host 'Install state:'
    Get-Content (Join-Path $root '.pbm/install-state.json')
}
