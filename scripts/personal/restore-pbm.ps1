param(
    [Parameter(Mandatory = $true)][string]$BackupFile,
    [switch]$Force
)

. (Join-Path $PSScriptRoot 'common.ps1')

Assert-PbmPrerequisites
Assert-PbmSecretsConfigured

$resolvedBackup = (Resolve-Path $BackupFile).Path
if (-not (Test-Path $resolvedBackup)) { throw "Backup file was not found: $BackupFile" }
if ([System.IO.Path]::GetExtension($resolvedBackup) -ne '.bak') { throw 'Backup file must have a .bak extension.' }

$metadataPath = "$resolvedBackup.json"
if (Test-Path $metadataPath) {
    $metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
    $currentHash = (Get-FileHash -Path $resolvedBackup -Algorithm SHA256).Hash
    if ($metadata.sha256 -and $metadata.sha256 -ne $currentHash) {
        throw 'Backup SHA256 does not match its metadata. Restore aborted.'
    }
}

if (-not $Force) {
    $answer = Read-Host 'This will replace the current PBM database. Type RESTORE to continue'
    if ($answer -ne 'RESTORE') { throw 'Restore cancelled.' }
}

$backupDir = Get-PbmBackupDir
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
$targetBackup = Join-Path $backupDir ([System.IO.Path]::GetFileName($resolvedBackup))
if ($resolvedBackup -ne $targetBackup) { Copy-Item $resolvedBackup $targetBackup -Force }

Write-Host 'Stopping PBM API and Web...' -ForegroundColor Cyan
try { Invoke-PbmDockerCompose -Arguments @('stop', 'api', 'web') } catch { }
Invoke-PbmDockerCompose -Arguments @('up', '-d', 'db')

$containerPath = "/var/opt/mssql/backup/$([System.IO.Path]::GetFileName($targetBackup))"
$sql = "IF DB_ID(N'PerformanceBudgetManagement') IS NOT NULL BEGIN ALTER DATABASE [PerformanceBudgetManagement] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; END; RESTORE DATABASE [PerformanceBudgetManagement] FROM DISK = N'$containerPath' WITH REPLACE, CHECKSUM, STATS = 10; ALTER DATABASE [PerformanceBudgetManagement] SET MULTI_USER;"
$escapedSql = $sql.Replace('"', '\"')
$command = '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -Q "' + $escapedSql + '"'

Write-Host "Restoring database from $targetBackup ..." -ForegroundColor Cyan
Invoke-PbmDockerCompose -Arguments @('exec', '-T', 'db', 'bash', '-lc', $command)

Write-Host 'Starting PBM application...' -ForegroundColor Cyan
Invoke-PbmDockerCompose -Arguments @('up', '-d', 'api', 'web')
Wait-PbmReady -TimeoutSeconds 240
Write-Host 'PBM database restore completed successfully.' -ForegroundColor Green
