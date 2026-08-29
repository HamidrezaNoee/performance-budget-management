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

## Slice 3 — financial and performance modules (implemented / hardening)
- Personnel/headcount model
- Department / marketing / sales / administrative OPEX
- CAPEX/OPEX classification dimension
- Program, activity and project analysis dimensions
- Import landed-cost measures and calculated unit landed cost / gross margin
- Loans, repayments and finance-cost model
- P&L, balance-sheet and cash-flow import/reporting model
- KPI targets, actuals and scoring with Higher-is-Better / Lower-is-Better / Target-Range semantics
- Strategic-objective hierarchy and weighted KPI ↔ objective mappings
- Strategy-aware performance budgeting scorecard: KPI → objective → strategic score → funding recommendation
- Funding recommendation policy combines performance coverage with Actual / Commitment / Forecast exposure
- Multidimensional Budget-vs-Actual variance analysis workspace
- Rule-based variance anomaly baseline with configurable warning threshold in the UI
- Executive dashboard semantic metric priority instead of summing unrelated amount measures
- Selectable executive dashboard amount metrics with consistent Budget / Actual / Commitment / Forecast aggregation
- Dimension-aware executive drill-down with ranked member table and SQL Server integration coverage
- Budget reservation lifecycle: request, availability control, approval, rejection, release and consume
- Approved reservations post to Commitment with serializable approval-time availability recheck
- Reservation-to-Actual reconciliation monitoring for consumed reservations
- Governed budget transfer/reallocation lifecycle across open periods and multidimensional coordinates
- Transfer approval preserves total Budget, rechecks source availability under a serializable transaction and recalculates dependent formulas
- Governed assumption/driver catalog with company + fiscal-year scope and optional scenario/period overrides
- Formula variables support explicit `[ASSUMP:CODE]` references with deterministic scope resolution
- Assumption changes automatically recalculate affected unlocked Draft versions only
- Standard enterprise driver definitions are seeded without fake financial values
- Reusable driver templates for sales, payroll, import landed cost, financing and OPEX
- Formula Designer validates measure/assumption dependencies, self-reference and dependency cycles
- Dynamic measure creation and governed formula editing without redeployment
- CAPEX project lifecycle with Proposed → Submitted → Approved → InProgress/OnHold → Completed/Cancelled states
- Requested CAPEX and approved budget ceiling are stored independently with reviewer-only approval governance
- CAPEX projects automatically create a PROJECT dimension member and use the multidimensional BudgetFact model as financial source of truth
- Weighted CAPEX milestones drive physical completion and cannot be overridden through the API
- CAPEX financial and portfolio summaries separate currencies and surface overdue projects
- Dedicated RTL CAPEX workspace with project creation, review actions, milestones and Jalali display
- Dedicated cash planning / treasury workspace with Opening / Inflow / Outflow / Liquidity Buffer and rolling closing cash
- Cash planning keeps currency as an explicit required dimension and supports Budget / Actual / Commitment / Forecast
- Next: portfolio performance ranking across companies / organization units / programs

## Slice 4 — governance and enterprise integration (implemented / hardening)
- Budget approval state machine, revisions and review comments
- Company/organization administration and data-level access enforcement
- Audit trail for sensitive operations
- Scenario selection for revised budget versions
- Readiness/liveness endpoints and configurable per-client login throttling
- Migration-aware schema startup policy; production no longer silently uses `EnsureCreated`
- Initial EF Core migration committed; migration helper script and CI model-drift check included
- Supporting file attachments on budget versions/comments with size/type/hash validation
- Persistent in-app notification center with unread count and Jalali timestamps
- Workflow, reservation, transfer and CAPEX notifications with role/company targeting and deep links
- JWT token-version revocation after password, role or company-access changes
- Self-service password change followed by mandatory re-authentication
- Correlation ID and request tracing on API operations
- Durable request idempotency guard with SQL Server application locks and manual reconciliation for uncertain writes
- Immutable Actual Ledger for ERP/accounting source rows
- Source business key: SourceSystem + ExternalDocumentId + ExternalLineId with payload-conflict protection
- Actual Ledger Posting/Reversal semantics; no destructive update/delete of accounting source rows
- `BudgetFact.Actual` generated as a ledger-owned multidimensional projection with formula recalculation
- Ledger ↔ Actual projection reconciliation and controlled rebuild
- External-key posting API resolves PeriodCode, MeasureCode and DimensionMember ExternalKey/Code
- Retry-safe batch Actual ingestion with per-row result and up to 1000 rows per request
- Dedicated RTL Actual/ERP operations workspace with reversal and reconciliation controls
- Next: add database indexes/constraints tuned for Actual Ledger volume after the initial migration is generated
- Next: email/SMS/Teams notification delivery adapters and retry/outbox handling
- Next: BPMN/workflow engine adapter
- Next: AD/LDAP/Entra/SSO adapters
- Next: concrete ERP adapters (SQL pull, REST push, accounting-system profiles) on top of the Actual Ledger contract

## Slice 5 — analytics and AI
- Forecasting service (trend/statistical baseline)
- Variance ranking and drill-down
- Rule-based anomaly identification baseline
- Performance-based funding recommendation baseline with explainable reasons
- Next: statistical anomaly detection and driver/root-cause analysis
- Next: management narrative generation
- Next: governed AI assistant over PBM data
- Next: SSAS Tabular/Power BI semantic model and reporting integration

## Engineering hardening backlog
- Get GitHub Actions build/test execution enabled and green for the feature branch
- Keep EF migration drift checks green and add forward-only schema upgrade scripts for deployment
- Add SQL Server integration tests for transactions, authorization, assumptions, CAPEX, reservation/transfer concurrency, workbook imports and Actual Ledger concurrency/reversal
- Add explicit migration-time database indexes for high-volume Actual Ledger source identity and reconciliation queries
- Add trusted-proxy/forwarded-header configuration before relying on client-IP throttling behind a reverse proxy
- Expand structured observability with metrics/OpenTelemetry and integration-specific dashboards
- Add connector-level dead-letter/outbox handling for pull/push ERP adapters
