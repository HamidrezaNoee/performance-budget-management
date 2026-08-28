# Performance Budget Management (PBM)

Enterprise performance-budgeting platform for multi-company budgeting, actuals, KPI management, forecasting, multidimensional planning, approval governance and management reporting.

## Target architecture

- Backend: ASP.NET Core 10 / C#
- Frontend: React + TypeScript + Material UI
- Database: Microsoft SQL Server
- Architecture: Modular Monolith + Clean Architecture + DDD where it adds value
- Deployment: Docker / Windows Server / On-Premise
- Analytics: operational reporting first; Power BI / SSAS integration-ready
- UI: Persian-first RTL, bilingual-ready
- Dates: Jalali display with Gregorian storage

## Current platform scope

The current implementation includes multi-company security, fiscal calendars, multidimensional budget models, scenarios and scenario-aware revisions, editable planning grids, Excel inspection/import/normalization, Budget/Actual/Commitment/Forecast values, formula recalculation, prior-year baseline creation, bulk paste/spread, approval inbox and comments, KPI management, forecasting, variance analysis with a rule-based anomaly baseline, financial reports, audit trail, organization administration and license limits.

The model is flexible enough to support vehicle/import and distribution planning as well as departmental OPEX, personnel, financing, financial statements, projects/programs/activities and other enterprise budgeting domains.

## Local Docker startup

PBM does not store development passwords or JWT signing keys in tracked configuration.

1. Copy `.env.example` to `.env`.
2. Replace all `ChangeMe...` values with strong local secrets.
3. Start the stack:

```bash
docker compose up --build
```

The development compose profile explicitly enables demo seed data and `Database__AllowEnsureCreated=true`. This fallback exists only while the initial EF Core migration is being prepared. It also creates the bootstrap administrator using the credentials from `.env`.

- Web: `http://localhost:3000`
- API: `http://localhost:8080`
- Liveness: `http://localhost:8080/livez`
- Readiness: `http://localhost:8080/readyz`
- SQL Server: `localhost:1433`

To reset the local demo database completely:

```bash
docker compose down -v
docker compose up --build
```

## Database schema policy

PBM startup is migration-aware:

- If EF Core migrations exist and the database is current, startup continues normally.
- If migrations exist but pending migrations are found, startup fails unless `Database__AutoMigrate=true` is explicitly enabled.
- If no migrations exist, `EnsureCreated` is permitted only when the environment is Development **and** `Database__AllowEnsureCreated=true`.
- Production no longer silently creates an unmanaged schema with `EnsureCreated`.

Once the initial migration is committed, production deployments should normally apply migrations as a deployment step and keep both `Database__AutoMigrate=false` and `Database__AllowEnsureCreated=false`. Auto-migration remains an explicit deployment choice rather than a default.

## Production / first deployment bootstrap

Demo data is **disabled by default** and is blocked outside Development unless explicitly overridden. For a clean production database, configure the following through environment variables, user secrets, Kubernetes/Swarm secrets or another deployment secret store:

```text
ConnectionStrings__PbmDatabase
Jwt__Key
Jwt__Issuer
Jwt__Audience

Database__AutoMigrate=false
Database__AllowEnsureCreated=false

Bootstrap__ProvisionInitialTenant=true
Bootstrap__UseDemoSeed=false
Bootstrap__TenantCode
Bootstrap__TenantName
Bootstrap__CompanyCode
Bootstrap__CompanyName
Bootstrap__Industry
Bootstrap__LicenseKey
Bootstrap__LicenseDays
Bootstrap__MaxCompanies
Bootstrap__MaxUsers

BootstrapAdmin__UserName
BootstrapAdmin__DisplayName
BootstrapAdmin__Password

Cors__AllowedOrigins__0=https://pbm.example.com
RateLimiting__Login__PermitLimit=10
RateLimiting__Login__WindowSeconds=60
```

On an empty database, the production bootstrap creates only the initial tenant, company, license, generic dimensions/models, standard security roles and the first `SUPERADMIN`. It does **not** create the pharmaceutical/demo company, demo budget facts or workbook-specific department/account members.

After the initial deployment, keep the database and remove/disable first-provisioning settings where your deployment process permits it. Existing tenants/users are not recreated on subsequent application starts.

## Dashboard metric semantics

The executive dashboard no longer aggregates every monetary measure into one meaningless total. It selects the first available amount measure from the configured priority list under `Dashboard:PreferredAmountMeasureCodes` (for example `NET_SALES`, then `GROSS_SALES`). The priority is tenant-data-aware and can be changed in configuration without changing dashboard code.

## Security and operational notes

- The login screen never pre-fills or embeds the bootstrap password.
- Login throttling is partitioned by client IP and returns HTTP 429 with `Retry-After` when the configured limit is exceeded.
- Company read and write permissions are separate JWT claims and are enforced again on the server.
- Approval actions are role-aware and company-write-aware.
- Budget plan creation rejects duplicate company/year/model plans and accepts an explicit active scenario.
- Database uniqueness constraints also protect plan identity and version numbering.
- CORS is explicit in non-development environments.
- Tracked `appsettings.json` contains no database password, JWT signing key or bootstrap administrator password.
- `.env` files are ignored by Git; only `.env.example` is tracked.

## Engineering status

The repository contains a CI workflow and unit tests, but GitHub currently reports no workflow runs for the feature branch, so a green hosted build has not yet been confirmed. The application now has a production-safe migration policy, but the **initial EF Core migration itself still needs to be generated and committed with a .NET SDK/EF tooling environment** before a first production deployment.

Development work is performed on feature branches and proposed through pull requests.
