# Performance Budget Management (PBM)

Enterprise performance-budgeting platform for multi-company budgeting, actuals, KPI management, forecasting, multidimensional planning and management reporting.

## Target architecture

- Backend: ASP.NET Core 10 / C#
- Frontend: React + TypeScript
- Database: Microsoft SQL Server
- Architecture: Modular Monolith + Clean Architecture + DDD where it adds value
- Deployment: Docker / Windows Server / On-Premise
- Analytics: operational reporting first; Power BI / SSAS integration-ready
- UI: Persian-first RTL, bilingual-ready
- Dates: Jalali display with Gregorian storage

## Initial domain scope

The first vertical slice covers multi-company setup, fiscal periods, multidimensional budget models, budget versions/scenarios, measures/formulas, budget facts, actuals and a management dashboard. The domain is intentionally flexible enough to represent the supplied pharmaceutical budgeting workbook: product/company/month import-sales-inventory planning, personnel costs, departmental marketing/administrative/sales costs, loans and finance costs, P&L, balance sheet and cash-flow views.

Development work is performed on feature branches and proposed through pull requests.
