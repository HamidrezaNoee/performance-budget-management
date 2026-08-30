# ADR-006 — Immutable Actual Ledger for ERP/accounting execution data

## Status
Accepted for the initial PBM platform.

## Context
`BudgetFact` is the multidimensional analytical/operational projection used by planning grids, dashboards, formulas and reports. It is not a suitable source ledger for externally posted accounting rows because a fact cell is an aggregate and can be overwritten.

ERP/accounting integrations need stronger guarantees:
- a source document line must not be posted twice after retry;
- corrections must remain auditable;
- a source row must be traceable to the projected Actual cell;
- multiple service instances must not race on the same external line;
- manually entered/imported Actual must not be silently replaced by an ERP feed;
- projection drift must be detectable and repairable.

## Decision
PBM stores source-system Actual rows in an immutable `ActualLedgerEntry` ledger and treats `BudgetFact(ValueKind.Actual)` as a derived projection.

### Business identity
A posting is identified by:
`Tenant + Company + SourceSystem + ExternalDocumentId + ExternalLineId`.

The API computes a payload hash. Repeating the same identity with the same payload is a successful duplicate/no-op. Reusing the identity with a different payload is rejected and requires a governed reversal plus a new external line/revision identity.

HTTP `Idempotency-Key` remains available as an additional request-level guard, but the external business identity is the durable accounting guard.

### Concurrency
The posting service acquires a SQL Server `sp_getapplock` transaction lock derived from the external identity before checking/inserting the ledger row. This protects first-write races across multiple PBM API instances.

### Corrections
Ledger rows are never edited or deleted. `Reversal` creates a second immutable row whose amount is the exact negative of the original posting and references `OriginalEntryId`. A posting may be reversed only once. In-place reversal is blocked when the fiscal period is closed; a later adjustment must be posted into an approved open period instead.

### Projection
For each `Version + Period + Measure + CoordinateHash`, PBM sums all Posting and Reversal ledger amounts and writes one `BudgetFact` with:
- `ValueKind = Actual`
- `Source = ActualLedger`
- the ledger currency
- the original multidimensional coordinate.

Formula recalculation runs after projection.

### Ownership boundary
If an Actual `BudgetFact` already exists for a coordinate and is owned by another source such as manual entry or workbook import, the ledger refuses to take ownership silently. The coordinate must be explicitly reconciled/migrated first.

### Currency rule
The current `BudgetFact` uniqueness key does not include `CurrencyCode`. Therefore one business coordinate may contain only one ledger currency. Models that need simultaneous currencies at the same coordinate must include currency as a model dimension.

### External-key API
ERP clients do not need PBM GUIDs for every coordinate. `POST /api/v1/actual-ledger/post-by-key` accepts:
- `VersionId`
- `PeriodCode`
- `MeasureCode`
- dimension-code → member external-key/code pairs.

PBM resolves these to internal IDs and then uses the same immutable posting pipeline.

### Batch API
`POST /api/v1/actual-ledger/batch` accepts up to 1000 external-key rows. Each row is committed independently and carries its own durable business identity. With `ContinueOnError=true`, failures are returned per row. Replaying the batch is safe because already committed rows become duplicates/no-ops.

### Reconciliation
The operational console and API compare ledger totals with `BudgetFact.Actual` projections and classify:
- Reconciled
- MissingProjection
- AmountMismatch
- CurrencyMismatch
- ProjectionWithoutLedger.

Authorized finance/admin users can rebuild ledger-owned projections without modifying the immutable source ledger.

## Consequences
- ERP/accounting Actual becomes traceable and retry-safe.
- Budget planning facts remain optimized for multidimensional analysis.
- Source corrections preserve history.
- Projection repair is possible without re-importing source documents.
- The first EF Core migration must include the newly discovered ledger entities and should add database indexes/constraints appropriate for expected ERP volume.
