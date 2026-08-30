. (Join-Path $PSScriptRoot 'common.ps1')

Assert-PbmPrerequisites
Assert-PbmSecretsConfigured

$backupDir = Get-PbmBackupDir
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

Write-Host 'Ensuring SQL Server is running...' -ForegroundColor Cyan
Invoke-PbmDockerCompose -Arguments @('up', '-d', 'db')

$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupName = "PBM_$timestamp.bak"
$containerPath = "/var/opt/mssql/backup/$backupName"
$sql = @"
BACKUP DATABASE [PerformanceBudgetManagement]
TO DISK = N'$containerPath'
WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT, STATS = 10;
GO
RESTORE VERIFYONLY
FROM DISK = N'$containerPath'
WITH CHECKSUM;
GO
"@

# Avoid nested PowerShell -> Docker -> bash -> sqlcmd quoting issues by transporting
# the T-SQL as Base64 and piping the decoded script to sqlcmd on stdin.
$sqlBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($sql))
$command = "printf '%s' '$sqlBase64' | base64 -d | /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P `"`$MSSQL_SA_PASSWORD`" -C -b"

Write-Host "Creating verified backup: $backupName" -ForegroundColor Cyan
Invoke-PbmDockerCompose -Arguments @('exec', '-T', 'db', 'bash', '-lc', $command)

$hostPath = Join-Path $backupDir $backupName
if (-not (Test-Path $hostPath)) {
    throw "SQL Server reported success but the backup is not visible on the host: $hostPath"
}
$file = Get-Item $hostPath
if ($file.Length -le 0) {
    throw "Backup file is empty: $hostPath"
}

# RESTORE VERIFYONLY WITH CHECKSUM above is the authoritative integrity check.
# A newly provisioned and compressed SQL Server database can legitimately produce
# a backup smaller than 1 MB, so do not reject a valid backup by arbitrary size.
$hash = (Get-FileHash -Path $hostPath -Algorithm SHA256).Hash
$metadata = [ordered]@{
    createdAtUtc = [DateTime]::UtcNow.ToString('o')
    database = 'PerformanceBudgetManagement'
    file = $file.Name
    sizeBytes = $file.Length
    sha256 = $hash
    gitCommit = Get-PbmCurrentGitRef
    sqlVerifyOnly = $true
    checksumVerified = $true
}
$metadata | ConvertTo-Json | Set-Content -Path "$hostPath.json" -Encoding UTF8

Write-Host "Backup verified: $hostPath" -ForegroundColor Green
Write-Host "Size bytes: $($file.Length)"
Write-Host "SHA256: $hash"
Write-Output $hostPath
