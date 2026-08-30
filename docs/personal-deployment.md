# PBM Personal Production

This profile is intended for a single Windows workstation that will contain real PBM business data and may later be migrated to an enterprise server.

## Safety model

- Application containers are replaceable.
- SQL Server data lives in the named volume `pbm_personal_sql_data`.
- SQL backups are written to the host backup directory configured by `PBM_BACKUP_DIR`.
- Real-data installation is blocked until an EF Core model snapshot/initial migration exists.
- `Database__AllowEnsureCreated=false` is mandatory in the personal profile.
- Updates create and verify a SQL backup before application code is changed.
- Never run `docker compose down -v` against the personal installation.

## Preview before real-data readiness

Preview is intentionally disposable and uses the Development profile:

```powershell
Copy-Item .env.example .env
# Replace every ChangeMe value.
.\scripts\personal\start-preview.ps1
```

Open `http://localhost:3000`.

Do not enter business-critical data into Preview. Preview exists to validate UI and features while migrations/build hardening are still in progress.

## First real-data installation

This becomes available only after `src/PBM.Infrastructure/Migrations/*ModelSnapshot.cs` exists.

```powershell
Copy-Item .env.personal.example .env.personal
# Replace every CHANGE_ME value and set company identity.
.\scripts\personal\install-pbm.ps1
```

The installer refuses to proceed if migrations or required secrets are missing.

## Backup

```powershell
.\scripts\personal\backup-pbm.ps1
```

The script performs SQL Server `BACKUP DATABASE ... WITH CHECKSUM`, runs `RESTORE VERIFYONLY`, calculates SHA-256 on the host file, and writes adjacent JSON metadata containing the source Git commit.

## Status

```powershell
.\scripts\personal\status-pbm.ps1
```

## Update to a release/tag

```powershell
.\scripts\personal\update-pbm.ps1 -TargetRef v0.2.0
```

Update sequence:

1. mandatory verified database backup;
2. fetch and checkout the requested Git release/tag;
3. rebuild application images;
4. start the new API; startup applies committed EF migrations;
5. start Web;
6. wait for `/readyz`;
7. keep the pre-update backup permanently until the new version is accepted.

If readiness fails, API/Web are stopped. Do not continue entering data until the previous code and the pre-update database backup are restored.

## Restore

```powershell
.\scripts\personal\restore-pbm.ps1 -BackupFile .\.pbm\backups\PBM_YYYYMMDD_HHMMSS.bak
```

For a failed schema-changing update, restore the pre-update `.bak` and run the exact previous application commit/version. Do not assume application-only rollback is safe after a migration.

## Later migration to an enterprise server

1. Stop writes on the personal installation.
2. Create a verified final `.bak`.
3. Install the same PBM application version on the server.
4. Restore the database backup into server SQL Server.
5. Configure server-specific environment variables, URL, HTTPS, CORS and secrets.
6. Run readiness and reconciliation checks.
7. Only after validation, switch users/DNS to the server.
8. Perform any later PBM version upgrade as a separate operation.

Never combine workstation-to-server migration and an application/schema upgrade in the same cutover step.
