param(
    [string]$BackupFile
)

. (Join-Path $PSScriptRoot 'common.ps1')

Assert-PbmPrerequisites
Assert-PbmSecretsConfigured

$backupDir = Get-PbmBackupDir
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

if ([string]::IsNullOrWhiteSpace($BackupFile)) {
    $latest = Get-ChildItem -Path $backupDir -Filter '*.bak' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (-not $latest) { throw "No .bak files were found in $backupDir. Run backup-pbm.ps1 first." }
    $resolvedBackup = $latest.FullName
}
else {
    $resolvedBackup = (Resolve-Path $BackupFile).Path
}

if (-not (Test-Path $resolvedBackup)) { throw "Backup file was not found: $resolvedBackup" }
if ([System.IO.Path]::GetExtension($resolvedBackup) -ne '.bak') { throw 'Backup file must have a .bak extension.' }

$metadataPath = "$resolvedBackup.json"
if (Test-Path $metadataPath) {
    $metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
    $currentHash = (Get-FileHash -Path $resolvedBackup -Algorithm SHA256).Hash
    if ($metadata.sha256 -and $metadata.sha256 -ne $currentHash) {
        throw 'Backup SHA256 does not match its metadata. Restore verification aborted.'
    }
    Write-Host 'Backup metadata SHA256: PASSED' -ForegroundColor Green
}
else {
    Write-Warning 'Backup metadata file was not found. SQL VERIFYONLY and DBCC CHECKDB will still be executed.'
}

$temporaryCopy = $null
$sourceForMount = $resolvedBackup
$backupDirFull = [System.IO.Path]::GetFullPath($backupDir)
$resolvedDir = [System.IO.Path]::GetFullPath((Split-Path $resolvedBackup -Parent))
if (-not $resolvedDir.TrimEnd('\').Equals($backupDirFull.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    $temporaryCopy = Join-Path $backupDir ("restoretest_" + [Guid]::NewGuid().ToString('N') + '.bak')
    Copy-Item -Path $resolvedBackup -Destination $temporaryCopy -Force
    $sourceForMount = $temporaryCopy
}

$backupName = [System.IO.Path]::GetFileName($sourceForMount)
$containerBackupPath = "/var/opt/mssql/backup/$backupName"
$containerName = 'pbm-restore-test-' + (Get-Date -Format 'yyyyMMddHHmmss') + '-' + ([Guid]::NewGuid().ToString('N').Substring(0, 6))
$saPassword = Get-PbmEnvValue -Name 'PBM_SA_PASSWORD'
$tempEnvFile = Join-Path ([System.IO.Path]::GetTempPath()) ("pbm-restore-test-" + [Guid]::NewGuid().ToString('N') + '.env')

try {
    # SQLCMDPASSWORD lets sqlcmd authenticate without putting -P and the password
    # through Windows PowerShell -> docker -> bash quoting layers.
    @(
        'ACCEPT_EULA=Y',
        "MSSQL_SA_PASSWORD=$saPassword",
        "SQLCMDPASSWORD=$saPassword"
    ) | Set-Content -Path $tempEnvFile -Encoding ASCII

    Write-Host "Starting isolated SQL Server restore-test container: $containerName" -ForegroundColor Cyan
    $mountSpec = "type=bind,source=$backupDirFull,target=/var/opt/mssql/backup,readonly"
    & docker run -d --rm --name $containerName --env-file $tempEnvFile --mount $mountSpec mcr.microsoft.com/mssql/server:2022-latest *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start isolated SQL Server restore-test container.' }

    Write-Host 'Waiting for isolated SQL Server...' -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds(120)
    $ready = $false
    do {
        Start-Sleep -Seconds 2
        $probeExitCode = 1
        try {
            # Run sqlcmd directly (no bash and no -P) so SELECT 1 is passed as one
            # native argument even under Windows PowerShell 5.1. Startup failures are
            # expected briefly and are intentionally suppressed while we retry.
            & docker exec $containerName /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q 'SET NOCOUNT ON; SELECT 1;' *> $null
            $probeExitCode = $LASTEXITCODE
        }
        catch {
            $probeExitCode = 1
        }
        if ($probeExitCode -eq 0) { $ready = $true; break }
    } while ((Get-Date) -lt $deadline)

    if (-not $ready) {
        Write-Host 'Isolated SQL Server did not become ready. Recent container logs:' -ForegroundColor Yellow
        & docker logs --tail 40 $containerName
        throw 'Isolated SQL Server did not become ready within 120 seconds.'
    }

    $restoreSql = @"
RESTORE VERIFYONLY
FROM DISK = N'$containerBackupPath'
WITH CHECKSUM;
GO
RESTORE DATABASE [PerformanceBudgetManagement]
FROM DISK = N'$containerBackupPath'
WITH CHECKSUM, STATS = 10;
GO
DBCC CHECKDB ([PerformanceBudgetManagement]) WITH NO_INFOMSGS;
GO
USE [PerformanceBudgetManagement];
GO
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL THROW 51000, 'Tenants table is missing after restore.', 1;
IF OBJECT_ID(N'dbo.Companies', N'U') IS NULL THROW 51000, 'Companies table is missing after restore.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL THROW 51000, 'Users table is missing after restore.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THROW 51000, '__EFMigrationsHistory table is missing after restore.', 1;
DECLARE @TenantCount int = (SELECT COUNT(*) FROM dbo.Tenants);
DECLARE @CompanyCount int = (SELECT COUNT(*) FROM dbo.Companies);
DECLARE @UserCount int = (SELECT COUNT(*) FROM dbo.Users);
DECLARE @MigrationCount int = (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory);
IF @TenantCount < 1 THROW 51001, 'Restored database contains no tenants.', 1;
IF @CompanyCount < 1 THROW 51002, 'Restored database contains no companies.', 1;
IF @UserCount < 1 THROW 51003, 'Restored database contains no users.', 1;
IF @MigrationCount < 1 THROW 51004, 'Restored database contains no EF migration history.', 1;
SELECT
    @TenantCount AS TenantCount,
    @CompanyCount AS CompanyCount,
    @UserCount AS UserCount,
    @MigrationCount AS MigrationCount;
GO
"@

    # Transport the T-SQL as Base64. sqlcmd reads SQLCMDPASSWORD from the container
    # environment, so this command contains neither the password nor nested -P quotes.
    $restoreSqlBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($restoreSql))
    $command = "printf '%s' '$restoreSqlBase64' | base64 -d | /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b"

    Write-Host "Restoring and validating $backupName in the isolated container..." -ForegroundColor Cyan
    & docker exec $containerName bash -lc $command
    if ($LASTEXITCODE -ne 0) { throw 'Backup restore or integrity validation failed.' }

    Write-Host 'RESTORE VERIFYONLY: PASSED' -ForegroundColor Green
    Write-Host 'Isolated database restore: PASSED' -ForegroundColor Green
    Write-Host 'DBCC CHECKDB: PASSED' -ForegroundColor Green
    Write-Host 'Critical PBM data checks: PASSED' -ForegroundColor Green
    Write-Host 'Live PBM database was not modified.' -ForegroundColor Green
}
finally {
    if (Test-Path $tempEnvFile) { Remove-Item $tempEnvFile -Force -ErrorAction SilentlyContinue }
    & docker rm -f $containerName *> $null
    if ($temporaryCopy -and (Test-Path $temporaryCopy)) { Remove-Item $temporaryCopy -Force -ErrorAction SilentlyContinue }
}
