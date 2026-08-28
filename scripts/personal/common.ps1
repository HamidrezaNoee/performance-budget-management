Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PbmRepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
}

function Get-PbmComposeArgs {
    param([string]$Root = (Get-PbmRepoRoot))
    $envFile = Join-Path $Root '.env.personal'
    $composeFile = Join-Path $Root 'docker-compose.personal.yml'
    if (-not (Test-Path $envFile)) {
        throw "Missing .env.personal. Copy .env.personal.example to .env.personal and replace all CHANGE_ME values."
    }
    if (-not (Test-Path $composeFile)) { throw "Missing docker-compose.personal.yml." }
    return @('compose', '--env-file', $envFile, '-f', $composeFile)
}

function Invoke-PbmDockerCompose {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$Root = (Get-PbmRepoRoot)
    )
    $base = Get-PbmComposeArgs -Root $Root
    Push-Location $Root
    try {
        & docker @base @Arguments
        if ($LASTEXITCODE -ne 0) { throw "docker compose failed with exit code $LASTEXITCODE." }
    }
    finally { Pop-Location }
}

function Assert-PbmPrerequisites {
    param([switch]$RequireMigrations)
    foreach ($command in @('docker', 'git')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "$command is required and was not found in PATH."
        }
    }
    & docker version *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Docker engine is not running.' }

    if ($RequireMigrations) {
        $root = Get-PbmRepoRoot
        $migrationDir = Join-Path $root 'src/PBM.Infrastructure/Migrations'
        $snapshot = Get-ChildItem -Path $migrationDir -Filter '*ModelSnapshot.cs' -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $snapshot) {
            throw 'PBM Personal Production is blocked until the initial EF Core migration is generated and committed. Do not enter real data using EnsureCreated.'
        }
    }
}

function Assert-PbmSecretsConfigured {
    $root = Get-PbmRepoRoot
    $envFile = Join-Path $root '.env.personal'
    $text = Get-Content $envFile -Raw
    if ($text -match 'CHANGE_ME') {
        throw '.env.personal still contains CHANGE_ME placeholders.'
    }
}

function Wait-PbmReady {
    param([int]$TimeoutSeconds = 180)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-WebRequest -Uri 'http://localhost:3000/readyz' -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return }
        }
        catch { }
        Start-Sleep -Seconds 3
    } while ((Get-Date) -lt $deadline)
    throw "PBM readiness check did not become healthy within $TimeoutSeconds seconds."
}

function Get-PbmCurrentGitRef {
    $root = Get-PbmRepoRoot
    Push-Location $root
    try {
        $sha = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine current Git commit.' }
        return $sha
    }
    finally { Pop-Location }
}
