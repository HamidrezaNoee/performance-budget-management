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

The current implementation includes multi-company security, fiscal calendars, multidimensional budget models, scenarios and revisions, editable planning grids, Excel inspection/import/normalization, Budget/Actual/Commitment/Forecast values, formula recalculation, prior-year baseline creation, bulk paste/spread, approval inbox and comments, KPI management, forecasting, variance analysis, financial reports, audit trail, organization administration and license limits.

The model is flexible enough to support vehicle/import and distribution planning as well as departmental OPEX, personnel, financing, financial statements, projects/programs/activities and other enterprise budgeting domains.

## Local Docker startup

PBM no longer stores development passwords or JWT signing keys in tracked configuration.

1. Copy `.env.example` to `.env`.
2. Replace all `ChangeMe...` values with strong local secrets.
3. Start the stack:

```bash
docker compose up --build
```

The development compose profile explicitly enables the demo seed and creates the bootstrap administrator using the credentials from `.env`.

- Web: `http://localhost:3000`
- API: `http://localhost:8080`
- SQL Server: `localhost:1433`

To reset the local demo database completely:

```bash
docker compose down -v
docker compose up --build
```

## Production / first deployment bootstrap

Demo data is **disabled by default** and is blocked outside Development unless explicitly overridden. For a clean production database, configure the following through environment variables, user secrets, Kubernetes/Swarm secrets or another deployment secret store:

```text
ConnectionStrings__PbmDatabase
Jwt__Key
Jwt__Issuer
Jwt__Audience

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
```

On an empty database, the production bootstrap creates only the initial tenant, company, license, generic dimensions/models, standard security roles and the first `SUPERADMIN`. It does **not** create the pharmaceutical/demo company, demo budget facts or workbook-specific department/account members.

After the initial deployment, keep the database and remove/disable first-provisioning settings where your deployment process permits it. Existing tenants/users are not recreated on subsequent application starts.

## Security notes

- The login screen never pre-fills or embeds the bootstrap password.
- Company read and write permissions are separate JWT claims and are enforced again on the server.
- Approval actions are role-aware and company-write-aware.
- CORS is explicit in non-development environments.
- Tracked `appsettings.json` contains no database password, JWT signing key or bootstrap administrator password.
- `.env` files are ignored by Git; only `.env.example` is tracked.

## Engineering status

The repository contains a CI workflow and tests, but CI execution still needs to be confirmed on the feature branch. Production EF Core migrations are also still on the hardening backlog; startup currently uses `EnsureCreated` for the evolving schema.

Development work is performed on feature branches and proposed through pull requests.
