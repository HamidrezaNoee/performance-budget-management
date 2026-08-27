# ADR-002: Safe PBM formula language before DAX compatibility

Status: Accepted

## Context

Business users need configurable measures such as customs tariff = customs value × 5%, sales = quantity × price, inventory closing balance and KPI calculations. Full DAX execution inside the operational application would couple PBM to an analytical engine and significantly expand the security/semantic surface.

## Decision

PBM stores formulas on measure/KPI definitions and initially evaluates a deliberately small safe expression language. Version 1 supports measure references such as `[SALES_QTY]`, arithmetic, parentheses and `ABS`, `MIN`, `MAX`, `ROUND` functions. It never compiles or executes arbitrary C# or scripts.

A later semantic-model integration can translate supported PBM measures to SSAS/Power BI measures and allow a governed DAX layer for analytics while the operational calculation engine remains deterministic.
