# Supplied pharmaceutical workbook → PBM domain map

The supplied budgeting workbook contains 78 worksheets and mixes inputs, calculations, monthly planning and financial statements. PBM does not reproduce those sheets one-for-one. It models their reusable business grain so a change in a fact or dimension can immediately flow into higher-level views.

## Main workbook areas represented by the platform

| Workbook area | PBM representation |
|---|---|
| Base information | tenant/company, fiscal year, dimensions, members and model metadata |
| Import / sales / inventory by product and month | `TRADE` budget model with Product + optional Supplier dimensions and origin-purchase, import-cost, warehouse, sales and closing-stock measures |
| Product price and currency amounts | CPT unit price, FX rate, foreign-currency purchase amount, IRR purchase amount and sales-price measures |
| Customs, bank fee, insurance, order-registration cost | configurable input rates plus calculated cost measures; VAT amount remains an explicit input because the workbook does not define one unambiguous taxable-base rule for every case |
| Detailed purchase sheets | Budget/Actual/Commitment/Forecast facts at Product/Supplier/Period grain |
| Inventory movement | opening quantity/value, purchases, available quantity, paid sales/COGS, free issue, sample, waste and closing quantity/value |
| Detailed sales sheets | sales quantity, free-sales quantity, price, gross sales, discount, net sales, total COGS and gross margin |
| Personnel cost and HR | departmental/cost-center model |
| Marketing, medical, sales and head-office expenses | department/cost-center/account dimensions |
| Loans, repayments and finance costs | finance model |
| Other operating/non-operating income and expense | account-based financial facts |
| Profit & loss | calculated financial-statement view |
| Balance sheet, receivables/payables | financial position model/view |
| Monthly and cumulative cash flow | calculated cash-flow views |

## Dedicated origin-to-sale workspace

The application now exposes a dedicated **زنجیره خرید، واردات و فروش** workspace on top of the normalized `TRADE` model. It is divided into four workbook-aligned stages:

1. **خرید از مبدا** — CPT, FX rate, import quantity, foreign-currency purchase and IRR purchase value.
2. **ثبت سفارش، حمل و گمرک** — order-registration, bank, insurance, customs, VAT, international freight, clearance, inland transport and total landed cost to warehouse.
3. **تحویل و گردش انبار** — opening inventory, purchases, available stock, sales/COGS, free issue, sample, waste, total COGS and closing inventory.
4. **فروش و حاشیه سود** — quantity, price, gross sales, sales discount, net sales, total COGS and gross margin.

The same workspace can switch between `Budget`, `Actual`, `Commitment` and `Forecast`; it does not create a second transaction store. Values continue to be stored as `BudgetFact` rows and therefore remain compatible with approval, revisions, dashboards, variance analysis and reporting.

### Trade measures added from the workbook

Key additions include `CPT_UNIT_PRICE`, `FX_RATE`, `BASE_UNIT_COST`, `PURCHASE_IRR_AMOUNT`, `ORDER_REG_RATE`, `BANK_FEE_RATE`, `INSURANCE_RATE`, `CUSTOMS_TARIFF_RATE`, `VAT_RATE`, `VAT_AMOUNT`, `TRADE_LANDED_COST_TOTAL`, `TRADE_LANDED_COST_PER_UNIT`, `OPENING_VALUE`, `AVAILABLE_QTY`, `COGS_QTY`, `COGS_AMOUNT`, `FOC_COST`, `SAMPLE_AMOUNT`, `WASTE_AMOUNT`, `TOTAL_COGS_AMOUNT`, `CLOSING_VALUE`, `FOC_SALES_AMOUNT`, `SALES_DISCOUNT`, `NET_SALES`, `TRADE_GROSS_MARGIN` and `TRADE_GROSS_MARGIN_PERCENT`.

PBM percentage measures use percentage points (`5` means `5%`). Workbook imports that store rates as decimal fractions (`0.05` for `5%`) must normalize them during mapping/import rather than silently changing the semantic meaning of stored PBM facts.

## Scope boundary from the supplied workbook

The workbook provides budgeting and performance data for purchase, import cost, inventory and sales, but it does **not** provide a complete operational logistics document model such as origin country, Incoterm, purchase-order number, shipment/container number, ETA/arrival milestones, customs declaration number or warehouse receipt number. Those fields belong to a future operational procurement/logistics slice or an ERP integration and are not presented here as workbook-derived requirements.

## Important modeling rule

Months are rows in `FiscalPeriod`, not separate database columns or tables. Product, supplier, department, cost center, account, program, activity, currency, brand, customer, region and contract are dimensions. Numeric inputs and calculated outputs are measures. A `BudgetFact` stores one measure value for one version, one period and a set of dimension coordinates.

This removes the main structural weakness of the workbook: formulas and references spread across dozens of worksheets. The application can aggregate the same normalized facts into product, month, department, company, P&L, cash-flow and executive dashboard views.
