# ADR-002: Safe PBM formula language before DAX compatibility

Status: Accepted

## Context

Business users need configurable measures such as customs tariff = customs value × rate, sales = quantity × price, inventory closing balance, personnel cost and KPI calculations. Some drivers are not facts at a budget coordinate; they are governed assumptions such as exchange rates, inflation, salary growth or financing rates. Full DAX execution inside the operational application would couple PBM to an analytical engine and significantly expand the security/semantic surface.

## Decision

PBM stores formulas on measure/KPI definitions and evaluates a deliberately small safe expression language. Version 1 supports:

- measure references such as `[SALES_QTY]`
- governed assumption references such as `[ASSUMP:FX_USD]`
- arithmetic operators and parentheses
- `ABS`, `MIN`, `MAX` and `ROUND`

It never compiles or executes arbitrary C#, JavaScript, SQL or scripts.

Assumption codes use the interoperable form `A-Z`, `0-9` and `_`. The `ASSUMP:` namespace prevents collisions between budget measures and assumptions.

Example:

```text
[FOREIGN_COST] * [ASSUMP:FX_USD] * (1 + [ASSUMP:CUSTOMS_RATE])
```

Percent assumptions are stored as business-entered decimal values. Formula authors must be explicit about the desired representation. For example, if `INFLATION_RATE` is entered as `25`, use `(1 + [ASSUMP:INFLATION_RATE] / 100)`.

## Assumption resolution

An assumption value belongs to a company and fiscal year and can optionally be scoped to a budget scenario and/or fiscal period. For a budget version and period, PBM resolves the most specific available value in this order:

1. scenario + period
2. scenario + annual/default
3. global + period
4. global + annual/default

Changing an assumption automatically recalculates affected unlocked Draft versions only. Approved, closed or otherwise locked versions are never silently rewritten by an assumption change.

## Consequences

Driver-based budgeting can be introduced without duplicating assumptions as measures on every multidimensional coordinate. The operational engine remains deterministic and auditable while future semantic-model integration can translate supported PBM measures to SSAS/Power BI measures and expose a governed DAX layer for analytics.
