Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) {
    Copy-Item (Join-Path $root '.env.example') $envFile
    Write-Host 'Created .env from .env.example.' -ForegroundColor Yellow
    throw 'Edit .env and replace all ChangeMe values, then run this script again. Preview data is disposable and must not be treated as production data.'
}
if ((Get-Content $envFile -Raw) -match 'ChangeMe') {
    throw 'The .env file still contains ChangeMe placeholders.'
}
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker Desktop is required.' }
& docker version *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker engine is not running.' }

Push-Location $root
try {
    Write-Host 'Starting PBM disposable preview...' -ForegroundColor Cyan
    & docker compose up --build -d
    if ($LASTEXITCODE -ne 0) { throw 'docker compose failed.' }
}
finally { Pop-Location }

$deadline = (Get-Date).AddMinutes(4)
do {
    try {
        $response = Invoke-WebRequest -Uri 'http://localhost:3000/readyz' -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            Write-Host 'PBM Preview is ready: http://localhost:3000' -ForegroundColor Green
            Write-Host 'WARNING: this preview uses disposable Development schema/data. Do not enter business-critical real data.' -ForegroundColor Yellow
            exit 0
        }
    }
    catch { }
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline)

throw 'PBM preview did not become ready. Run docker compose logs api for diagnostics.'
