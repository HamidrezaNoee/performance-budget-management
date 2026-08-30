param(
    [ValidateSet('status', 'add-initial', 'add', 'bundle')]
    [string]$Action = 'status',
    [string]$MigrationName = 'InitialCreate',
    [string]$BundleOutput = 'artifacts/pbm-efbundle'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$infrastructureProject = Join-Path $repoRoot 'src/PBM.Infrastructure/PBM.Infrastructure.csproj'
$startupProject = Join-Path $repoRoot 'src/PBM.Api/PBM.Api.csproj'
$migrationsDirectory = Join-Path $repoRoot 'src/PBM.Infrastructure/Migrations'

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE: dotnet $($Arguments -join ' ')"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK is required. Install .NET 10 SDK before running this script.'
}

if (-not (Test-Path $infrastructureProject) -or -not (Test-Path $startupProject)) {
    throw 'PBM project files were not found. Run this script from the repository checkout.'
}

if ([string]::IsNullOrWhiteSpace($env:PBM_DESIGNTIME_CONNECTION)) {
    # EF migration model operations need a provider connection string, but migration generation and
    # pending-model checks do not need to connect to a production database.
    $env:PBM_DESIGNTIME_CONNECTION = 'Server=127.0.0.1,1433;Database=PBM_DesignTime;User Id=pbm_design;Password=DesignTime_NotForConnection_Only!123;Encrypt=True;TrustServerCertificate=True;Connection Timeout=1'
}

Push-Location $repoRoot
try {
    Invoke-DotNet tool restore

    switch ($Action) {
        'status' {
            if (-not (Test-Path $migrationsDirectory)) {
                throw 'No EF Core migrations directory exists. Generate and commit the initial migration before production deployment.'
            }

            $snapshot = Get-ChildItem -Path $migrationsDirectory -Filter '*ModelSnapshot.cs' -File -ErrorAction SilentlyContinue | Select-Object -First 1
            $migrationFiles = Get-ChildItem -Path $migrationsDirectory -Filter '*.cs' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -notlike '*ModelSnapshot.cs' -and $_.Name -notlike '*.Designer.cs' }
            if (-not $snapshot -or -not $migrationFiles) {
                throw 'EF Core migration snapshot or migration files are missing.'
            }

            Invoke-DotNet ef migrations has-pending-model-changes `
                --project $infrastructureProject `
                --startup-project $startupProject
            Write-Host 'EF migration status is clean: committed migrations match the current model.' -ForegroundColor Green
        }
        'add-initial' {
            if (Test-Path $migrationsDirectory) {
                $existingMigrations = Get-ChildItem -Path $migrationsDirectory -Filter '*.cs' -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -notlike '*ModelSnapshot.cs' -and $_.Name -notlike '*.Designer.cs' }
                if ($existingMigrations) {
                    throw 'At least one migration already exists. Use -Action add for subsequent schema changes.'
                }
            }

            Invoke-DotNet ef migrations add $MigrationName `
                --project $infrastructureProject `
                --startup-project $startupProject `
                --output-dir Migrations
            Write-Host "Initial migration '$MigrationName' generated. Review the SQL/schema diff before committing." -ForegroundColor Green
        }
        'add' {
            if ([string]::IsNullOrWhiteSpace($MigrationName) -or $MigrationName -eq 'InitialCreate') {
                throw 'Provide a descriptive -MigrationName for a subsequent migration.'
            }
            if (-not (Test-Path $migrationsDirectory)) {
                throw 'Initial migration is missing. Use -Action add-initial first.'
            }

            Invoke-DotNet ef migrations add $MigrationName `
                --project $infrastructureProject `
                --startup-project $startupProject `
                --output-dir Migrations
            Write-Host "Migration '$MigrationName' generated. Review it before committing." -ForegroundColor Green
        }
        'bundle' {
            & $PSCommandPath -Action status
            if ($LASTEXITCODE -ne 0) { throw 'Migration status validation failed.' }

            $bundlePath = if ([System.IO.Path]::IsPathRooted($BundleOutput)) {
                $BundleOutput
            } else {
                Join-Path $repoRoot $BundleOutput
            }
            $bundleDirectory = Split-Path -Parent $bundlePath
            if ($bundleDirectory -and -not (Test-Path $bundleDirectory)) {
                New-Item -ItemType Directory -Path $bundleDirectory -Force | Out-Null
            }

            Invoke-DotNet ef migrations bundle `
                --project $infrastructureProject `
                --startup-project $startupProject `
                --output $bundlePath `
                --force
            Write-Host "Migration bundle created at $bundlePath" -ForegroundColor Green
        }
    }
}
finally {
    Pop-Location
}
