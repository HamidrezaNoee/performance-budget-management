# Resolve the initial PBM production blocker

PBM Personal Production requires a committed EF Core initial migration. The host machine does **not** need the .NET SDK; Docker Desktop is sufficient.

## Why this gate exists

Real business data must never start on an unmanaged `EnsureCreated` schema. Personal Production therefore blocks installation until all of the following succeed:

1. .NET 10 Release build
2. unit tests
3. EF Core `InitialCreate` generation
4. pending-model-change check
5. idempotent SQL generation
6. API production Docker build
7. Web/TypeScript production Docker build

## One-command generation

From PowerShell in the repository root on branch `feat/initial-platform`:

```powershell
.\scripts\resolve-production-blocker.ps1 -Action generate-initial
```

The script uses `mcr.microsoft.com/dotnet/sdk:10.0` inside Docker when executing EF tooling. It writes the generated migration files into:

```text
src/PBM.Infrastructure/Migrations/
```

It also creates:

```text
artifacts/pbm-schema-idempotent.sql
```

If any build, test or EF validation fails, the script stops and Personal Production remains blocked.

## After successful generation

Review the generated migration and commit the migration directory. Then run:

```powershell
.\scripts\resolve-production-blocker.ps1 -Action verify
```

After verification succeeds, create `.env.personal` from `.env.personal.example` and install:

```powershell
Copy-Item .env.personal.example .env.personal
# Edit every CHANGE_ME value.
.\scripts\personal\install-pbm.ps1
```

`install-pbm.ps1` runs the same verification gate again before it can touch the Personal Production database.

## Data-safety rule

Once real data is entered, never use `docker compose down -v` against the Personal Production compose profile. Application containers are replaceable; the SQL persistent volume and verified backups are the durable assets.
