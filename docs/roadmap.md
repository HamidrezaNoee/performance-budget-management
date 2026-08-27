# PBM delivery roadmap

## Slice 1 — platform foundation (implemented)
- Multi-tenant / multi-company model and license limits
- Fiscal years and Jalali periods with Gregorian storage
- Dynamic dimensions and hierarchical members
- Budget models, measures, formulas, scenarios, versions and facts
- Budget / Actual / Commitment / Forecast value kinds
- JWT login + role/company access model
- Dashboard API and RTL React dashboard
- SQL Server + Docker + CI + unit tests

## Slice 2 — planning grid and workbook intake (in progress)
- Editable multidimensional planning grid with month columns
- Dynamic row dimension and fixed dimension filters
- Direct fact upsert from grid cells
- XLSX workbook inspection, sheet discovery and preview
- Next: mapping profiles from legacy workbook columns/rows to PBM dimensions/measures
- Next: copy prior-year actual to budget baseline, bulk paste/fill/spread and version comparison
- Next: formula dependency graph and recalculation
- Next: currency source/rate management

## Slice 3 — financial and performance modules
- Personnel-cost model
- Department / marketing / sales / administrative OPEX
- CAPEX/OPEX classification
- Import landed-cost model
- Loans, repayments and finance cost
- P&L, balance sheet and cash-flow statements
- KPI targets, actuals, weighted scoring and drill-down

## Slice 4 — governance and enterprise integration
- Approval workflow/BPMN integration
- Audit trail and comments/attachments
- Notification rules (in-app/email/SMS/Teams adapters)
- AD/LDAP/Entra/SSO adapters
- ERP/accounting/SQL/API actual-data connectors
- Row-level/data-level authorization enforcement

## Slice 5 — analytics and AI
- Forecasting service (trend/statistical/AI)
- anomaly and variance detection
- management narrative generation
- governed AI assistant over PBM data
- SSAS Tabular/Power BI semantic model and reporting integration
