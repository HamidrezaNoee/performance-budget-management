. (Join-Path $PSScriptRoot 'common.ps1')

Assert-PbmPrerequisites -RequireMigrations
Assert-PbmSecretsConfigured
Assert-PbmPersonalInstallPorts

$root = Get-PbmRepoRoot
$verifyScript = Join-Path $root 'scripts/resolve-production-blocker.ps1'
if (-not (Test-Path $verifyScript)) { throw 'Missing scripts/resolve-production-blocker.ps1.' }

Write-Host 'Verifying Release build, unit tests and EF migration drift in Dockerized .NET 10 SDK...' -ForegroundColor Cyan
& $verifyScript -Action verify
if ($LASTEXITCODE -ne 0) { throw 'PBM verification failed. Personal Production installation is blocked.' }

$backupDir = Join-Path $root '.pbm/backups'
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

Write-Host 'Building PBM Personal Production images...' -ForegroundColor Cyan
Invoke-PbmDockerCompose -Arguments @('build')

Write-Host 'Starting SQL Server, API and Web...' -ForegroundColor Cyan
try {
    Invoke-PbmDockerCompose -Arguments @('up', '-d')
}
catch {
    Write-Host ''
    Write-Host 'PBM startup failed. Container status:' -ForegroundColor Red
    try { Invoke-PbmDockerCompose -Arguments @('ps') } catch { }
    Write-Host ''
    Write-Host 'Recent SQL Server logs:' -ForegroundColor Yellow
    try { Invoke-PbmDockerCompose -Arguments @('logs', '--tail', '120', 'db') } catch { }
    throw
}

Write-Host 'Waiting for PBM readiness...' -ForegroundColor Cyan
try {
    Wait-PbmReady -TimeoutSeconds 240
}
catch {
    Write-Host ''
    Write-Host 'PBM readiness failed. Container status:' -ForegroundColor Red
    try { Invoke-PbmDockerCompose -Arguments @('ps') } catch { }
    Write-Host ''
    Write-Host 'Recent API logs:' -ForegroundColor Yellow
    try { Invoke-PbmDockerCompose -Arguments @('logs', '--tail', '120', 'api') } catch { }
    throw
}

$stateDir = Join-Path $root '.pbm'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$state = [ordered]@{
    installedAtUtc = [DateTime]::UtcNow.ToString('o')
    gitCommit = Get-PbmCurrentGitRef
    composeProfile = 'personal'
    database = 'PerformanceBudgetManagement'
    persistentVolume = 'pbm_personal_sql_data'
}
$state | ConvertTo-Json | Set-Content -Path (Join-Path $stateDir 'install-state.json') -Encoding UTF8

Write-Host ''
Write-Host 'PBM Personal Production is ready.' -ForegroundColor Green
Write-Host "Open: $(Get-PbmWebUrl)"
Write-Host 'Important: never run docker compose down -v for this installation.' -ForegroundColor Yellow
