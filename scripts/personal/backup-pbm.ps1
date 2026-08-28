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
$sql = "BACKUP DATABASE [PerformanceBudgetManagement] TO DISK = N'$containerPath' WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT, STATS = 10; RESTORE VERIFYONLY FROM DISK = N'$containerPath' WITH CHECKSUM;"
$command = "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"`$MSSQL_SA_PASSWORD\" -C -b -Q \"$sql\""

Write-Host "Creating verified backup: $backupName" -ForegroundColor Cyan
Invoke-PbmDockerCompose -Arguments @('exec', '-T', 'db', 'bash', '-lc', $command)

$hostPath = Join-Path $backupDir $backupName
if (-not (Test-Path $hostPath)) {
    throw "SQL Server reported success but the backup is not visible on the host: $hostPath"
}
$file = Get-Item $hostPath
if ($file.Length -lt 1MB) {
    throw "Backup file is unexpectedly small ($($file.Length) bytes): $hostPath"
}

$hash = (Get-FileHash -Path $hostPath -Algorithm SHA256).Hash
$metadata = [ordered]@{
    createdAtUtc = [DateTime]::UtcNow.ToString('o')
    database = 'PerformanceBudgetManagement'
    file = $file.Name
    sizeBytes = $file.Length
    sha256 = $hash
    gitCommit = Get-PbmCurrentGitRef
}
$metadata | ConvertTo-Json | Set-Content -Path "$hostPath.json" -Encoding UTF8

Write-Host "Backup verified: $hostPath" -ForegroundColor Green
Write-Host "SHA256: $hash"
Write-Output $hostPath
