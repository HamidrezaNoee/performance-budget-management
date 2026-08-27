# PBM delivery roadmap

## Slice 1 — platform foundation (current)
- Multi-tenant / multi-company model and license limits
- Fiscal years and Jalali periods with Gregorian storage
- Dynamic dimensions and hierarchical members
- Budget models, measures, formulas, scenarios, versions and facts
- Budget / Actual / Commitment / Forecast value kinds
- JWT login + role/company access model
- Dashboard API and RTL React dashboard
- SQL Server + Docker + CI + unit tests

## Slice 2 — budget grid and workbook import
- Excel import wizard with sheet/column mapping
- Editable pivot-style planning grid
- Copy prior-year actual to budget baseline
- Bulk paste, fill, spread and percentage increase/decrease
- Formula dependency graph and recalculation
- Currency source/rate management
- Version comparison and revision history

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
