param(
    [ValidateSet('verify', 'generate-initial', 'rebuild-initial')]
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
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [string]$ConnectionString = $designTimeConnection,
        [string]$Network = ''
    )

    # Windows PowerShell here-strings use CRLF. Normalize to LF and Base64-wrap the
    # script so quoting, paths and multiline commands survive Windows -> Docker.
    $normalizedCommand = ($Command -replace "`r`n", "`n") -replace "`r", ''
    $commandBytes = [System.Text.Encoding]::UTF8.GetBytes($normalizedCommand)
    $encodedCommand = [Convert]::ToBase64String($commandBytes)
    $containerCommand = "printf '%s' '$encodedCommand' | base64 -d | bash"

    $mount = "$repoRoot`:/workspace"
    $args = @(
        'run', '--rm',
        '-e', "PBM_DESIGNTIME_CONNECTION=$ConnectionString",
        '-v', $mount,
        '-w', '/workspace'
    )
    if (-not [string]::IsNullOrWhiteSpace($Network)) {
        $args += @('--network', $Network)
    }
    $args += @('mcr.microsoft.com/dotnet/sdk:10.0', 'bash', '-lc', $containerCommand)

    Write-Host 'Running .NET 10 SDK in Docker...' -ForegroundColor Cyan
    & docker @args
    if ($LASTEXITCODE -ne 0) {
        throw "Dockerized .NET SDK command failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ContainerBuildPreflight {
    Push-Location $repoRoot
    try {
        Write-Host 'Building PBM API production container...' -ForegroundColor Cyan
        & docker build -f src/PBM.Api/Dockerfile -t pbm-api-preflight:local .
        if ($LASTEXITCODE -ne 0) { throw 'PBM API Docker build failed.' }

        Write-Host 'Building PBM Web production container...' -ForegroundColor Cyan
        & docker build -f src/PBM.Web/Dockerfile -t pbm-web-preflight:local .
        if ($LASTEXITCODE -ne 0) { throw 'PBM Web Docker build failed.' }
    }
    finally { Pop-Location }
}

function Get-InitialMigrationFiles {
    if (-not (Test-Path $migrationsDirectory)) { return @() }
    return @(Get-ChildItem -Path $migrationsDirectory -Filter '*.cs' -File -ErrorAction SilentlyContinue)
}

function Test-TemporarySqlServerReady {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerName,
        [Parameter(Mandatory = $true)][string]$Password
    )

    # sqlcmd writes normal connection failures to stderr while SQL Server is still booting.
    # With $ErrorActionPreference='Stop', Windows PowerShell can promote that stderr to a
    # NativeCommandError and abort the retry loop. Temporarily suppress native stderr so a
    # non-zero exit code simply means "not ready yet".
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'SilentlyContinue'
        & docker exec $ContainerName /opt/mssql-tools18/bin/sqlcmd `
            -S localhost -U sa -P $Password -C -b -l 2 -Q 'SELECT 1' 2>&1 | Out-Null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return ($exitCode -eq 0)
}

function Invoke-MigrationSqlServerSmokeTest {
    $suffix = [Guid]::NewGuid().ToString('N').Substring(0, 10)
    $network = "pbm-migration-smoke-$suffix"
    $dbContainer = "pbm-migration-smoke-db-$suffix"
    $password = 'PBM_Smoke_Only!2026Aa987654321'
    $dbImage = 'mcr.microsoft.com/mssql/server:2022-latest'
    $networkCreated = $false
    $containerCreated = $false

    try {
        Write-Host 'Applying committed EF migrations to a clean temporary SQL Server...' -ForegroundColor Cyan
        & docker network create $network *> $null
        if ($LASTEXITCODE -ne 0) { throw 'Could not create migration smoke-test Docker network.' }
        $networkCreated = $true

        & docker run -d --name $dbContainer --network $network `
            -e 'ACCEPT_EULA=Y' `
            -e "MSSQL_SA_PASSWORD=$password" `
            $dbImage *> $null
        if ($LASTEXITCODE -ne 0) { throw 'Could not start temporary SQL Server for migration smoke test.' }
        $containerCreated = $true

        $ready = $false
        Write-Host 'Waiting for temporary SQL Server to accept connections...' -ForegroundColor DarkCyan
        for ($i = 1; $i -le 90; $i++) {
            if (Test-TemporarySqlServerReady -ContainerName $dbContainer -Password $password) {
                $ready = $true
                Write-Host "Temporary SQL Server is ready after $i attempt(s)." -ForegroundColor DarkCyan
                break
            }

            if (($i % 10) -eq 0) {
                $running = (& docker inspect -f '{{.State.Running}}' $dbContainer 2>$null).Trim()
                if ($running -ne 'true') {
                    Write-Host 'Temporary SQL Server stopped unexpectedly. Logs:' -ForegroundColor Yellow
                    & docker logs --tail 120 $dbContainer
                    throw 'Temporary SQL Server container stopped before becoming ready.'
                }
                Write-Host "Still waiting for SQL Server... attempt $i/90" -ForegroundColor DarkGray
            }
            Start-Sleep -Seconds 2
        }
        if (-not $ready) {
            Write-Host 'Temporary SQL Server logs:' -ForegroundColor Yellow
            & docker logs --tail 120 $dbContainer
            throw 'Temporary SQL Server did not become ready for migration smoke test.'
        }

        # The integration fixture deliberately refuses databases whose names do not start with
        # PBM_Integration. Reuse the same disposable database after the migration application test;
        # the fixture recreates it before executing the SQL-backed application/seed tests.
        $smokeConnection = "Server=$dbContainer,1433;Database=PBM_IntegrationSmoke_$suffix;User Id=sa;Password=$password;Encrypt=False;TrustServerCertificate=True;Connection Timeout=30"
        $smokeCommand = @'
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
dotnet tool restore
dotnet restore src/PBM.Api/PBM.Api.csproj
dotnet ef database update --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj --no-build || {
  dotnet build src/PBM.Api/PBM.Api.csproj -c Debug --no-restore
  dotnet ef database update --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj --no-build
}

echo 'Running SQL-backed PBM integration tests, including seed/bootstrap paths...'
export PBM_INTEGRATION_SQL="$PBM_DESIGNTIME_CONNECTION"
dotnet restore tests/PBM.Integration.Tests/PBM.Integration.Tests.csproj
dotnet test tests/PBM.Integration.Tests/PBM.Integration.Tests.csproj -c Release --no-restore
'@
        Invoke-DotnetSdkContainer -Command $smokeCommand -ConnectionString $smokeConnection -Network $network
        Write-Host 'SQL Server migration and integration smoke tests: PASSED' -ForegroundColor Green
    }
    finally {
        if ($containerCreated) { & docker rm -f $dbContainer *> $null }
        if ($networkCreated) { & docker network rm $network *> $null }
    }
}

Assert-Docker

$restoreAndBuild = @'
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
mkdir -p artifacts
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

    $command = $restoreAndBuild + "`n" + @'
dotnet ef migrations has-pending-model-changes --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj
dotnet ef migrations script --idempotent --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj --output artifacts/pbm-schema-idempotent.sql
'@
    Invoke-DotnetSdkContainer -Command $command
    Invoke-MigrationSqlServerSmokeTest
    Invoke-ContainerBuildPreflight
    Write-Host 'PBM backend/frontend builds passed, EF model matches committed migrations, migrations apply to clean SQL Server, and SQL integration tests pass.' -ForegroundColor Green
    Write-Host 'Generated deployment SQL: artifacts/pbm-schema-idempotent.sql'
    exit 0
}

if ($Action -eq 'rebuild-initial') {
    $installState = Join-Path $repoRoot '.pbm/install-state.json'
    if (Test-Path $installState) {
        throw 'Initial migration rebuild is blocked because a successful Personal Production installation state exists. Create a normal follow-up migration instead.'
    }

    $existingForRebuild = @(Get-InitialMigrationFiles)
    if ($existingForRebuild.Count -eq 0) {
        throw 'No existing initial migration was found to rebuild.'
    }

    Write-Host 'Rebuilding the initial migration before first successful production installation...' -ForegroundColor Yellow
    Remove-Item -Path $migrationsDirectory -Recurse -Force
    New-Item -ItemType Directory -Path $migrationsDirectory -Force | Out-Null
}
else {
    # Always force collection semantics. PowerShell may unwrap zero/one pipeline results.
    $existing = @(Get-InitialMigrationFiles | Where-Object {
        $_.Name -notlike '*ModelSnapshot.cs' -and $_.Name -notlike '*.Designer.cs'
    })
    if ($existing.Count -gt 0) {
        throw 'A migration already exists. Use -Action verify instead of generating another initial migration.'
    }
}

$escapedMigrationName = $MigrationName -replace "'", "''"
$command = $restoreAndBuild + "`n" + @"
dotnet ef migrations add '$escapedMigrationName' --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj --output-dir Migrations
dotnet ef migrations has-pending-model-changes --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj
dotnet ef migrations script --idempotent --project src/PBM.Infrastructure/PBM.Infrastructure.csproj --startup-project src/PBM.Api/PBM.Api.csproj --output artifacts/pbm-schema-idempotent.sql
"@

Invoke-DotnetSdkContainer -Command $command
Invoke-MigrationSqlServerSmokeTest
Invoke-ContainerBuildPreflight

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
Write-Host 'Backend Release build: passed'
Write-Host 'Frontend production build: passed'
Write-Host 'Unit tests: passed'
Write-Host 'Migration drift check: passed'
Write-Host 'SQL Server migration smoke test: passed'
Write-Host 'SQL Server integration tests: passed'
Write-Host 'Idempotent SQL script: artifacts/pbm-schema-idempotent.sql'
Write-Host ''
Write-Host 'Review the generated migration, then commit src/PBM.Infrastructure/Migrations before entering real data.' -ForegroundColor Yellow
