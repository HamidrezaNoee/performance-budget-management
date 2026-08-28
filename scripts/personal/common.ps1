Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PbmRepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
}

function Get-PbmEnvValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Default = ''
    )
    $root = Get-PbmRepoRoot
    $envFile = Join-Path $root '.env.personal'
    if (-not (Test-Path $envFile)) { return $Default }
    foreach ($line in Get-Content $envFile) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) { continue }
        $parts = $trimmed -split '=', 2
        if ($parts.Count -eq 2 -and $parts[0].Trim() -eq $Name) {
            return $parts[1].Trim().Trim('"').Trim("'")
        }
    }
    return $Default
}

function Get-PbmBackupDir {
    $root = Get-PbmRepoRoot
    $configured = Get-PbmEnvValue -Name 'PBM_BACKUP_DIR' -Default './.pbm/backups'
    if ([System.IO.Path]::IsPathRooted($configured)) { return $configured }
    return [System.IO.Path]::GetFullPath((Join-Path $root $configured))
}

function Get-PbmWebUrl {
    $port = Get-PbmEnvValue -Name 'PBM_WEB_PORT' -Default '3000'
    return "http://localhost:$port"
}

function Get-PbmComposeArgs {
    param([string]$Root = (Get-PbmRepoRoot))
    $envFile = Join-Path $Root '.env.personal'
    $composeFile = Join-Path $Root 'docker-compose.personal.yml'
    if (-not (Test-Path $envFile)) {
        throw "Missing .env.personal. Copy .env.personal.example to .env.personal and replace all active CHANGE_ME values."
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
    if (-not (Test-Path $envFile)) {
        throw 'Missing .env.personal. Copy .env.personal.example first.'
    }

    $requiredSecrets = @(
        'PBM_SA_PASSWORD',
        'PBM_JWT_KEY',
        'PBM_ADMIN_PASSWORD'
    )

    foreach ($name in $requiredSecrets) {
        $value = Get-PbmEnvValue -Name $name
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw ".env.personal is missing a value for $name."
        }
        if ($value -match 'CHANGE_ME') {
            throw ".env.personal still contains a placeholder value for $name."
        }
    }

    $jwtKey = Get-PbmEnvValue -Name 'PBM_JWT_KEY'
    if ($jwtKey.Length -lt 64) {
        throw 'PBM_JWT_KEY must contain at least 64 characters.'
    }
}

function Wait-PbmReady {
    param([int]$TimeoutSeconds = 180)
    $url = "$(Get-PbmWebUrl)/readyz"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return }
        }
        catch { }
        Start-Sleep -Seconds 3
    } while ((Get-Date) -lt $deadline)
    throw "PBM readiness check did not become healthy within $TimeoutSeconds seconds: $url"
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
