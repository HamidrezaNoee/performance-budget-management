# PBM refactor audit — phase 1

Baseline: `feat/initial-platform` at `9f8c2579b173edb50023936cf927006abaa80f1e`.
Refactor branch: `feat/refactor-cleanup`. This branch is **not** deployed or merged into Personal Production.

## Confirmed duplication and cleanup

| Area | Evidence | Change in this branch |
| --- | --- | --- |
| Login component | The login/captcha screen was embedded in the large `App.tsx` application shell. | Extracted unchanged login flow into `LoginPage.tsx`; removed its unused imports from `App.tsx`. |
| UI styles | Login rules were duplicated across `styles.css` and `login-ai.css`, alongside the image data stylesheet. | Collected login layout and enhanced rules in `login.css`; kept global fonts/workspace styles in `styles.css`; kept the generated background in `login-background.css`. Some base/enhancement declarations still intentionally cascade inside the consolidated login file; reducing those further requires visual regression checks. |
| Settings access checks | The same role-to-visibility predicates were repeated in `ReferenceAdmin.tsx` and `SystemSettingsWorkspace.tsx`. | Reused `resolveSettingsPermissions` from `settingsPermissions.ts`. This is UI visibility only, not server authorization. |
| Historical one-off patch | `scripts/fix-frontend-select-types.ps1` rewrote a specific obsolete Select expression in four known files, not a normal build/upgrade step. | Removed it from the active tree; Git history retains it. |
| Stale operator guidance | Root README said the initial EF migration did not exist and CI had no workflow runs. | Updated the statement and emphasized demo-only volume reset. |

## Do not delete without business validation

- `CatalogPlaceholder.tsx`, portions of `NavigationTrees.ts` and the settings/master-data routes identify screens with no completed model/API. A visible placeholder is **not evidence that the underlying business requirement is unnecessary**. Decide which sections to implement or hide only after comparing them to the intended product scope.
- Similar financial dashboards, forecasts, planning services and integration endpoints operate on different domains or data flows; do not delete them merely because their names resemble each other.
- Keep EF migrations (including `InitialCreate` and `OperationalMasterData`), the snapshot, SQL schema/configuration and persistent Docker volumes. Do not regenerate or squash applied migrations for an installation with live data.
- Preserve API paths, JWT/captcha requirements, browser login behavior, saved user sessions, Persian UI and licensed local font installation.

## Technical debt for later phases

1. Break up the remaining large `App.tsx` workspace/navigation shell and large planning/admin components behind stable props and routes. Do this screen by screen with regression tests.
2. Replace hard-coded nested placeholder route mappings with a capability registry after scope review; do not silently remove menu functionality.
3. Review browser-session architecture: the current JWT is readable by browser JavaScript/localStorage and mirrored into a JavaScript-readable cookie for the Nginx fallback. Do not describe it as HttpOnly. Any change to cookie/session handling needs HTTPS/CSRF/threat-model work and API-client compatibility tests.
4. Assess CAPTCHA abuse resistance and storage behavior for multi-instance server deployments separately from cosmetic cleanup.
5. Consider backend endpoint/service splitting only after a map of dependencies and SQL integration tests; database/data-model cleanup must use **new forward-only migrations** with verified rollback plans.

## Required checks before merging or deploying

- Build frontend using `docker build -f src/PBM.Web/Dockerfile -t pbm-web-refactor-check .` from a separate checkout.
- Build backend, run domain and SQL integration tests, and verify EF migration drift before any backend changes.
- Check login with valid/invalid/expired CAPTCHA, password, browser reload, logout and a non-browser Bearer client.
- Inspect RTL/responsive login, generated background, local IranYekan fonts, settings visibility for SUPERADMIN/ADMIN/AUDITOR/CFO/BUDGET_MANAGER/ordinary user.
- For deployments with real data: verified backup, isolated restore test, version compatibility and readiness before cutover.

**Validation status:** GitHub Actions runs for this refactor branch are currently marked failed with no job steps recorded; no successful frontend build or full regression suite has been observed. Do not merge, redeploy, or claim this refactor is production-ready until the checks above pass.
