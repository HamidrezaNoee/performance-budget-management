# Supplied pharmaceutical workbook → PBM domain map

The supplied budgeting workbook contains 78 worksheets and mixes inputs, calculations, monthly planning and financial statements. PBM does not reproduce those sheets one-for-one. It models their reusable business grain so a change in a fact or dimension can immediately flow into higher-level views.

## Main workbook areas represented by the platform

| Workbook area | PBM representation |
|---|---|
| Base information | tenant/company, fiscal year, dimensions, members and model metadata |
| Import / sales / inventory by product and month | `TRADE` budget model with Product + Supplier dimensions and opening, sales, import, sample, waste and closing measures |
| Product price and currency amounts | measure definitions + Currency dimension/rates (next slice) |
| Customs, bank fee, insurance, order-registration cost | configurable calculated measures/formulas |
| Detailed purchase and sales sheets | budget/actual facts at Product/Supplier/Period grain |
| Inventory movement | opening/import/sales/free/sample/waste/closing measures |
| Personnel cost and HR | departmental/cost-center model (next slice) |
| Marketing, medical, sales and head-office expenses | department/cost-center/account dimensions (next slice) |
| Loans, repayments and finance costs | finance model (next slice) |
| Other operating/non-operating income and expense | account-based financial facts |
| Profit & loss | calculated financial-statement view |
| Balance sheet, receivables/payables | financial position model/view |
| Monthly and cumulative cash flow | calculated cash-flow views |

## Important modeling rule

Months are rows in `FiscalPeriod`, not separate database columns or tables. Product, supplier, department, cost center, account, program, activity, currency, brand, customer, region and contract are dimensions. Numeric inputs and calculated outputs are measures. A `BudgetFact` stores one measure value for one version, one period and a set of dimension coordinates.

This removes the main structural weakness of the workbook: formulas and references spread across dozens of worksheets. The application can aggregate the same normalized facts into product, month, department, company, P&L, cash-flow and executive dashboard views.
