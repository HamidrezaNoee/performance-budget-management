# Multidimensional purchase forecasting

PBM stores purchase forecasts in the existing normalized budgeting fact model. No purchase-specific fact table or schema migration is required.

## Forecast measures

The `TRADE` budget model contains four manual forecast measures:

| Code | Purpose |
|---|---|
| `PURCHASE_FORECAST_QTY` | forecast purchase quantity |
| `PURCHASE_FORECAST_AMOUNT` | forecast base purchase amount in tenant base currency |
| `PURCHASE_COST_AMOUNT` | forecast amount for one purchase-cost type |
| `PURCHASE_COST_RATE` | percentage rate for one purchase-cost type |

All values created by the purchase forecast workspace are stored with `ValueKind.Forecast`.

## Dimensions

`PRODUCT` remains mandatory. The TRADE model can additionally use optional dimensions including Supplier, Brand, Currency, Contract, Region, Department, Cost Center, Account, Program, Activity, Project and Funding Source.

The user only supplies dimensions at which the forecast should be split. For example, these are distinct coordinates:

- Product A + Supplier X + Brand B
- Product A + Supplier Y + Brand B
- Product A + Supplier X + Contract C

Omitted optional dimensions are not inserted into the coordinate hash, so a high-level forecast and a more detailed forecast remain separate facts.

## User-defined purchase costs

`PURCHASECOST` is a dedicated dimension. The system provisions common members such as freight, insurance, bank fee, order registration, customs, VAT, clearance, inland transport and inspection. Authorized company writers can create additional company-specific members without a database migration.

A cost fact uses the selected base forecast dimensions plus exactly one `PURCHASECOST` member. This allows the same cost type to be analyzed by product, supplier, brand, contract, project, organization unit or any other attached TRADE dimension.

## Percentage-driven costs

When a user records `PURCHASE_COST_RATE`, PBM calculates:

`PURCHASE_COST_AMOUNT = PURCHASE_FORECAST_AMOUNT * PURCHASE_COST_RATE / 100`

for the same fiscal period and exact dimension coordinate. If the base purchase amount is later changed, all rate-driven cost amounts at that coordinate and period are recalculated.

A user can alternatively enter `PURCHASE_COST_AMOUNT` directly when the cost is a fixed amount rather than a percentage.

## API

- `GET /api/v1/purchase-forecast/setup`
- `POST /api/v1/purchase-forecast/cost-types`
- `POST /api/v1/purchase-forecast/query`
- `POST /api/v1/purchase-forecast/cell`

The query endpoint uses exact coordinate hashes rather than the generic planning grid aggregation. This avoids ambiguity when two forecasts share the same product and month but differ on optional dimensions.

## UI

The workspace route is `#purchase-forecast`, displayed as **پیش‌بینی خرید چندبعدی**. It provides:

- dynamic dimension selectors,
- monthly quantity and amount forecast entry,
- user-defined purchase-cost types,
- amount/percentage cost entry modes,
- automatic rate-based cost calculation,
- monthly and annual purchase totals.
