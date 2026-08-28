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

    # Docker Compose expands $NAME and ${NAME} inside unquoted/double-quoted .env values.
    # Reject that shape before Compose can silently alter a password/key. Single-quoted values are literal.
    foreach ($line in Get-Content $envFile) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) { continue }
        $parts = $trimmed -split '=', 2
        if ($parts.Count -ne 2) { continue }
        $name = $parts[0].Trim()
        if ($requiredSecrets -notcontains $name) { continue }
        $rawValue = $parts[1].Trim()
        $isSingleQuoted = $rawValue.Length -ge 2 -and $rawValue.StartsWith("'") -and $rawValue.EndsWith("'")
        if (-not $isSingleQuoted -and $rawValue.Contains('$')) {
            throw "$name contains a dollar sign that Docker Compose would interpolate. Wrap the entire value in single quotes in .env.personal, or regenerate the secret without a dollar sign."
        }
    }

    $jwtKey = Get-PbmEnvValue -Name 'PBM_JWT_KEY'
    if ($jwtKey.Length -lt 64) {
        throw 'PBM_JWT_KEY must contain at least 64 characters.'
    }
}

function Test-PbmTcpPortAvailable {
    param([Parameter(Mandatory = $true)][int]$Port)
    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $listener) {
            try { $listener.Stop() } catch { }
        }
    }
}

function Assert-PbmPersonalInstallPorts {
    $sqlPortText = Get-PbmEnvValue -Name 'PBM_SQL_PORT' -Default '14330'
    $webPortText = Get-PbmEnvValue -Name 'PBM_WEB_PORT' -Default '3000'

    $sqlPort = 0
    $webPort = 0
    if (-not [int]::TryParse($sqlPortText, [ref]$sqlPort) -or $sqlPort -lt 1 -or $sqlPort -gt 65535) {
        throw "PBM_SQL_PORT is invalid: $sqlPortText"
    }
    if (-not [int]::TryParse($webPortText, [ref]$webPort) -or $webPort -lt 1 -or $webPort -gt 65535) {
        throw "PBM_WEB_PORT is invalid: $webPortText"
    }

    # If our own service is already running, re-running install should remain idempotent.
    $ownDbRunning = $false
    $ownWebRunning = $false
    try {
        $base = Get-PbmComposeArgs
        $root = Get-PbmRepoRoot
        Push-Location $root
        try {
            $dbId = (& docker @base ps -q db 2>$null | Select-Object -First 1)
            if ($dbId) { $ownDbRunning = ((& docker inspect -f '{{.State.Running}}' $dbId 2>$null).Trim() -eq 'true') }
            $webId = (& docker @base ps -q web 2>$null | Select-Object -First 1)
            if ($webId) { $ownWebRunning = ((& docker inspect -f '{{.State.Running}}' $webId 2>$null).Trim() -eq 'true') }
        }
        finally { Pop-Location }
    }
    catch { }

    if (-not $ownDbRunning -and -not (Test-PbmTcpPortAvailable -Port $sqlPort)) {
        throw "PBM_SQL_PORT $sqlPort is unavailable on localhost. Set PBM_SQL_PORT=14330 (or another free port) in .env.personal before installation."
    }
    if (-not $ownWebRunning -and -not (Test-PbmTcpPortAvailable -Port $webPort)) {
        throw "PBM_WEB_PORT $webPort is unavailable on localhost. Set PBM_WEB_PORT to another free port in .env.personal before installation."
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
