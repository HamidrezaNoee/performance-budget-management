# PBM delivery roadmap

## Slice 1 — platform foundation (implemented)
- Multi-tenant / multi-company model and license limits
- Fiscal years and Jalali periods with Gregorian storage
- Dynamic dimensions and hierarchical members
- Budget models, measures, formulas, scenarios, versions and facts
- Budget / Actual / Commitment / Forecast value kinds
- JWT login + role/company read/write access model
- Dashboard API and RTL React dashboard
- SQL Server + Docker + CI definition + unit tests

## Slice 2 — planning grid and workbook intake (implemented / hardening)
- Editable multidimensional planning grid with month columns
- Dynamic row dimension and fixed dimension filters
- Direct fact upsert from grid cells
- XLSX workbook inspection, sheet discovery and preview
- Legacy workbook normalization profiles and automatic imports
- Copy prior-year Actual into the new Budget baseline with growth/decline adjustment
- Bulk paste and row-level period spreading
- Version comparison with absolute and percentage variance
- Formula dependency recalculation after cell edits and workbook imports
- Manual full-version recalculation
- Currency definitions, multiple FX-rate sources and dated rates
- Closed fiscal year/period enforcement for direct entry and imports
- Scenario-aware initial plan API and scenario-aware revisions

## Slice 3 — financial and performance modules (in progress)
- Personnel/headcount model
- Department / marketing / sales / administrative OPEX
- CAPEX/OPEX classification dimension
- Program, activity and project analysis dimensions
- Import landed-cost measures and calculated unit landed cost / gross margin
- Loans, repayments and finance-cost model
- P&L, balance-sheet and cash-flow import/reporting model
- KPI targets, actuals and scoring
- Multidimensional Budget-vs-Actual variance analysis workspace
- Rule-based variance anomaly baseline with configurable warning threshold in the UI
- Executive dashboard semantic metric priority instead of summing unrelated amount measures
- Budget reservation lifecycle: request, availability control, approval, rejection, release and consume
- Approved reservations post to Commitment with serializable approval-time availability recheck
- Reservation workspace is scoped by selected fiscal year and supports role-aware decisions
- Governed budget transfer/reallocation lifecycle across open periods and multidimensional coordinates
- Transfer approval preserves total Budget, rechecks source availability under a serializable transaction and recalculates dependent formulas
- Dedicated RTL transfer workspace with fiscal-year filtering, approval actions and notification deep links
- Governed assumption/driver catalog with company + fiscal-year scope and optional scenario/period overrides
- Formula variables support explicit `[ASSUMP:CODE]` references with deterministic scope resolution
- Assumption changes automatically recalculate affected unlocked Draft versions only
- Standard enterprise driver definitions are seeded without fake financial values
- Dedicated RTL assumptions workspace for annual/periodic and global/scenario-specific values
- Formula Designer validates measure/assumption dependencies, self-reference and dependency cycles
- Dynamic measure creation and governed formula editing without redeployment
- CAPEX project lifecycle with Proposed → Submitted → Approved → InProgress/OnHold → Completed/Cancelled states
- Requested CAPEX and approved budget ceiling are stored independently with reviewer-only approval governance
- CAPEX projects automatically create a PROJECT dimension member and use the multidimensional BudgetFact model as financial source of truth
- Weighted CAPEX milestones drive physical completion and cannot be overridden through the API
- CAPEX financial summary exposes Budget / Actual / Commitment / Forecast / Available by fiscal month
- CAPEX portfolio summary separates currencies, surfaces overdue projects and aggregates workflow status counts
- Dedicated RTL CAPEX workspace with project creation, review actions, milestones, Jalali display and portfolio KPIs
- Next: reusable driver-based budgeting templates for sales, payroll, import landed cost and financing
- Next: dedicated cash planning / treasury workspace
- Next: selectable executive dashboard metric and dimensional drill-down

## Slice 4 — governance and enterprise integration
- Budget approval state machine, revisions and review comments
- Company/organization administration and data-level access enforcement
- Audit trail for sensitive operations
- Scenario selection for revised budget versions
- Readiness/liveness endpoints and configurable per-client login throttling
- Migration-aware schema startup policy; production no longer silently uses `EnsureCreated`
- Database uniqueness rules for one plan per company/year/model and version numbers per plan
- Supporting file attachments on budget versions/comments with size/type/hash validation
- Persistent in-app notification center with unread count and Jalali timestamps
- Workflow, reservation, transfer and CAPEX notifications with role/company targeting and deep links
- JWT token-version revocation after password, role or company-access changes
- Self-service password change followed by mandatory re-authentication
- Next: generate and commit the initial EF Core migration and schema upgrade scripts
- Next: email/SMS/Teams notification delivery adapters and retry/outbox handling
- Next: BPMN/workflow engine adapter
- Next: AD/LDAP/Entra/SSO adapters
- Next: ERP/accounting/SQL/API actual-data connectors and reservation-to-actual reconciliation

## Slice 5 — analytics and AI
- Forecasting service (trend/statistical baseline)
- Variance ranking and drill-down
- Rule-based anomaly identification baseline
- Next: statistical anomaly detection and driver/root-cause analysis
- Next: management narrative generation
- Next: governed AI assistant over PBM data
- Next: SSAS Tabular/Power BI semantic model and reporting integration

## Engineering hardening backlog
- Get GitHub Actions build/test execution enabled and green for the feature branch
- Generate and commit the initial EF Core migration using a .NET SDK + EF tooling environment
- Add SQL Server integration tests for transactions, authorization, assumption scope resolution, CAPEX workflow/portfolio aggregation, reservation/transfer concurrency and workbook imports
- Add API idempotency keys for externally retried write requests and bulk operations
- Add reservation/ERP reconciliation monitoring for consumed reservations that have not yet produced Actual
- Add trusted-proxy/forwarded-header configuration before relying on client-IP throttling behind a reverse proxy
- Expand structured observability with correlation IDs, request tracing and metrics
