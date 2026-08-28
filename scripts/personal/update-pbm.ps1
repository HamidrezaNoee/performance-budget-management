param(
    [Parameter(Mandatory = $true)][string]$TargetRef
)

. (Join-Path $PSScriptRoot 'common.ps1')

Assert-PbmPrerequisites -RequireMigrations
Assert-PbmSecretsConfigured

$root = Get-PbmRepoRoot
$previousRef = Get-PbmCurrentGitRef

Write-Host 'Creating mandatory pre-update backup...' -ForegroundColor Cyan
$backupPath = & (Join-Path $PSScriptRoot 'backup-pbm.ps1') | Select-Object -Last 1
if (-not $backupPath -or -not (Test-Path $backupPath)) {
    throw 'Pre-update backup did not complete successfully. Update aborted.'
}

Push-Location $root
try {
    Write-Host "Fetching target release: $TargetRef" -ForegroundColor Cyan
    & git fetch --tags origin
    if ($LASTEXITCODE -ne 0) { throw 'git fetch failed.' }
    & git rev-parse --verify "$TargetRef^{commit}" *> $null
    if ($LASTEXITCODE -ne 0) { throw "Target ref does not exist: $TargetRef" }
    & git checkout --detach $TargetRef
    if ($LASTEXITCODE -ne 0) { throw "Cannot checkout target ref: $TargetRef" }
}
finally { Pop-Location }

$updateState = [ordered]@{
    startedAtUtc = [DateTime]::UtcNow.ToString('o')
    previousCommit = $previousRef
    targetRef = $TargetRef
    targetCommit = Get-PbmCurrentGitRef
    backupFile = $backupPath
}
$stateDir = Join-Path $root '.pbm'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$updateState | ConvertTo-Json | Set-Content -Path (Join-Path $stateDir 'last-update.json') -Encoding UTF8

try {
    Write-Host 'Building the new application version...' -ForegroundColor Cyan
    Invoke-PbmDockerCompose -Arguments @('build')

    Write-Host 'Starting the new version. EF migrations will be applied by the API startup policy...' -ForegroundColor Cyan
    Invoke-PbmDockerCompose -Arguments @('up', '-d')
    Wait-PbmReady -TimeoutSeconds 300

    Write-Host "PBM update completed successfully: $TargetRef" -ForegroundColor Green
    Write-Host "Backup kept at: $backupPath"
}
catch {
    Write-Host ''
    Write-Host 'UPDATE FAILED. The application is being stopped to protect the database.' -ForegroundColor Red
    try { Invoke-PbmDockerCompose -Arguments @('stop', 'api', 'web') } catch { }
    Write-Host "Pre-update application commit: $previousRef" -ForegroundColor Yellow
    Write-Host "Pre-update database backup: $backupPath" -ForegroundColor Yellow
    Write-Host 'Do not continue entering data. Restore the backup with restore-pbm.ps1, then checkout the previous commit before restarting.' -ForegroundColor Yellow
    throw
}
