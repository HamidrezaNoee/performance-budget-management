# PBM refactor audit — 2026-09-19

Baseline: feat/initial-platform at commit 9f8c2579b173edb50023936cf927006abaa80f1e.

## Ten review passes

1. **Repository integrity** — reconstructed and inspected all 326 tracked files; no generated build or dependency folders are tracked.
2. **Architecture boundaries** — Domain has no outward project dependency; Application references Domain only; no Domain/Application source imports Infrastructure or API.
3. **Backend duplication** — consolidated repeated company read/write authorization into one tested Application policy and removed local copies across Infrastructure services.
4. **Frontend duplication** — extracted login/session responsibilities from the application shell and replaced eager page imports with lazy modules.
5. **Dead and placeholder UI** — removed the placeholder-only component and removed or consolidated navigation items that did not provide working persistence/API behavior.
6. **Security** — stopped mirroring the bearer token into a JavaScript-readable cookie; authentication state is tab-scoped and legacy local-storage state is migrated once.
7. **Multi-company isolation** — retained tenant checks and centralized company-scope checks; SuperAdmin override behavior is covered by unit tests.
8. **Build determinism** — added the npm lockfile; Docker and CI now use npm ci.
9. **Performance** — split route modules and vendor groups. The initial application chunk fell from about 1.42 MB to about 34 KB; heavy dashboards load on demand.
10. **Verification** — TypeScript and Vite production build pass; diff whitespace check passes; duplicate-code scan improved from 65 clones / 2.31% to 59 clones / 2.02%.

## Functional cleanup

- The previously unreferenced portfolio financial and dimension dashboards are now connected to the executive dashboard for management roles.
- Settings navigation now exposes only working administration screens.
- Master-data navigation now exposes only implemented entities and workflows.

## Remaining verification

The GitHub Actions run created for the refactor ended before any workflow step started, and GitHub produced no job log. This is an account/runner configuration issue rather than a compile or test result. Backend build, SQL integration tests and EF migration drift verification must be rerun after GitHub Actions execution is enabled.
