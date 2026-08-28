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
- Next: dedicated CAPEX project lifecycle and cash planning
- Next: richer driver-based budgeting templates and assumptions workspace
- Next: selectable executive dashboard metric and dimensional drill-down
- Next: budget transfer/reallocation request lifecycle with source/destination controls

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
- Workflow and reservation notifications with role/company targeting and deep links
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
- Add SQL Server integration tests for transactions, authorization, reservation concurrency and workbook imports
- Add API idempotency keys for externally retried write requests and bulk operations
- Add reservation/ERP reconciliation monitoring for consumed reservations that have not yet produced Actual
- Add trusted-proxy/forwarded-header configuration before relying on client-IP throttling behind a reverse proxy
- Expand structured observability with correlation IDs, request tracing and metrics
