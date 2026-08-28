param(
    [ValidateSet('verify', 'generate-initial')]
    [string]$Action = 'verify',
    [string]$MigrationName = 'InitialCreate'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$migrationsDirectory = Join-Path $repoRoot 'src/PBM.Infrastructure/Migrations'
$designTimeConnection = 'Server=127.0.0.1,1433;Database=PBM_DesignTime;User Id=sa;Password=DesignTime_NotForConnection_Only!123;Encrypt=False;TrustServerCertificate=True;Connection Timeout=1'

function Assert-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker Desktop is required. Install Docker Desktop and ensure docker.exe is available in PATH.'
    }
    & docker version *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Docker engine is not running.' }
}

function Invoke-DotnetSdkContainer {
    param([Parameter(Mandatory = $true)][string]$Command)

    $mount = "$repoRoot`:/workspace"
    Write-Host "Running .NET 10 SDK in Docker..." -ForegroundColor Cyan
    & docker run --rm `
        -e "PBM_DESIGNTIME_CONNECTION=$designTimeConnection" `
        -v $mount `
        -w /workspace `
        mcr.microsoft.com/dotnet/sdk:10.0 `
        bash -lc $Command

    if ($LASTEXITCODE -ne 0) {
        throw "Dockerized .NET SDK command failed with exit code $LASTEXITCODE."
    }
}

function Get-InitialMigrationFiles {
    if (-not (Test-Path $migrationsDirectory)) { return @() }
    return @(Get-ChildItem -Path $migrationsDirectory -Filter '*.cs' -File -ErrorAction SilentlyContinue)
}

Assert-Docker

$restoreAndBuild = @'
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
dotnet tool restore
dotnet restore PerformanceBudgetManagement.slnx
dotnet build PerformanceBudgetManagement.slnx -c Release --no-restore
dotnet test tests/PBM.Domain.Tests/PBM.Domain.Tests.csproj -c Release --no-build
'@

if ($Action -eq 'verify') {
    $snapshot = Get-ChildItem -Path $migrationsDirectory -Filter '*ModelSnapshot.cs' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $snapshot) {
        throw "Initial EF migration is still missing. Run: .\scripts\resolve-production-blocker.ps1 -Action generate-initial"
    }

    $command = $restoreAndBuild + @'
dotnet ef migrations has-pending-model-changes --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj
dotnet ef migrations script --idempotent --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj --output artifacts/pbm-schema-idempotent.sql
'@
    Invoke-DotnetSdkContainer -Command $command
    Write-Host 'PBM backend build/tests passed and the EF model matches committed migrations.' -ForegroundColor Green
    Write-Host 'Generated deployment SQL: artifacts/pbm-schema-idempotent.sql'
    exit 0
}

$existing = Get-InitialMigrationFiles | Where-Object { $_.Name -notlike '*ModelSnapshot.cs' -and $_.Name -notlike '*.Designer.cs' }
if ($existing.Count -gt 0) {
    throw 'A migration already exists. Use -Action verify instead of generating another initial migration.'
}

$escapedMigrationName = $MigrationName -replace "'", "''"
$command = $restoreAndBuild + @"
dotnet ef migrations add '$escapedMigrationName' --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj --output-dir Migrations
dotnet ef migrations has-pending-model-changes --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj
dotnet ef migrations script --idempotent --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj --output artifacts/pbm-schema-idempotent.sql
"@

Invoke-DotnetSdkContainer -Command $command

$snapshot = Get-ChildItem -Path $migrationsDirectory -Filter '*ModelSnapshot.cs' -File -ErrorAction SilentlyContinue | Select-Object -First 1
$migration = Get-ChildItem -Path $migrationsDirectory -Filter '*.cs' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike '*ModelSnapshot.cs' -and $_.Name -notlike '*.Designer.cs' } |
    Select-Object -First 1
if (-not $snapshot -or -not $migration) {
    throw 'dotnet ef returned success but the expected migration/snapshot files were not created.'
}

Write-Host ''
Write-Host 'PRIMARY PRODUCTION BLOCKER RESOLVED LOCALLY.' -ForegroundColor Green
Write-Host "Migration: $($migration.Name)"
Write-Host "Snapshot: $($snapshot.Name)"
Write-Host 'Build: Release passed'
Write-Host 'Unit tests: passed'
Write-Host 'Migration drift check: passed'
Write-Host 'Idempotent SQL script: artifacts/pbm-schema-idempotent.sql'
Write-Host ''
Write-Host 'Review the generated migration, then commit src/PBM.Infrastructure/Migrations before entering real data.' -ForegroundColor Yellow
