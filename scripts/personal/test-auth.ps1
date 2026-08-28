. (Join-Path $PSScriptRoot 'common.ps1')

Assert-PbmPrerequisites
Assert-PbmSecretsConfigured

$webUrl = Get-PbmWebUrl
$userName = Get-PbmEnvValue -Name 'PBM_ADMIN_USERNAME' -Default 'admin'
$password = Get-PbmEnvValue -Name 'PBM_ADMIN_PASSWORD'

Write-Host "Testing PBM login through $webUrl as '$userName' ..." -ForegroundColor Cyan

$body = @{
    userName = $userName
    password = $password
} | ConvertTo-Json -Compress

try {
    $login = Invoke-RestMethod -Uri "$webUrl/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 15
}
catch {
    $status = $null
    try { $status = [int]$_.Exception.Response.StatusCode } catch { }
    if ($status -eq 429) {
        throw 'Login smoke test was rate-limited (HTTP 429). Wait at least 60 seconds without retrying login, then run this script again.'
    }
    if ($status -eq 401) {
        throw "Login smoke test failed with HTTP 401 for user '$userName'. The configured bootstrap credentials do not match the stored account."
    }
    throw "Login smoke test failed before a token was issued. HTTP status: $status. $($_.Exception.Message)"
}

if ([string]::IsNullOrWhiteSpace([string]$login.accessToken)) {
    throw 'Login returned success but did not include an access token.'
}
Write-Host 'Login endpoint: PASSED (token received; token value is not displayed).' -ForegroundColor Green

$headers = @{ Authorization = "Bearer $($login.accessToken)" }
try {
    $companies = @(Invoke-RestMethod -Uri "$webUrl/api/v1/companies" -Method Get -Headers $headers -TimeoutSec 15)
}
catch {
    $status = $null
    try { $status = [int]$_.Exception.Response.StatusCode } catch { }
    if ($status -eq 401) {
        throw 'Protected API smoke test failed with HTTP 401 after a successful login. The bearer token is not reaching the API correctly or JWT validation rejected it.'
    }
    throw "Protected API smoke test failed. HTTP status: $status. $($_.Exception.Message)"
}

Write-Host "Protected endpoint /api/v1/companies: PASSED ($($companies.Count) company record(s))." -ForegroundColor Green
Write-Host 'PBM authentication smoke test: PASSED' -ForegroundColor Green
