# ADR-004 — Governed post-approval budget transfers

## Status
Accepted

## Context
Approved budget versions are intentionally read-only in the planning grid, but real organizations still require formally approved reallocation between months, cost centers, programs, projects or other budget dimensions after approval. Unlocking the version for direct editing would destroy the distinction between the approved baseline and an authorized transfer.

## Decision
1. Direct edits remain prohibited for approved/final budget versions.
2. Reallocation is performed only through the `BudgetTransfer` workflow.
3. A transfer stays in `Requested` state until a CFO/CEO/administrator decision; no Budget facts change while it is pending.
4. Source and destination must use the same tenant, company, budget version, amount measure and fiscal year. Period and dimension members may differ.
5. Both fiscal periods must be open and the fiscal year must be open.
6. Source availability is calculated as `Budget - Actual - Commitment` for the exact source coordinate.
7. Approval runs under a serializable transaction and rechecks source availability immediately before posting.
8. Approved posting subtracts the amount from the source `Budget` fact and adds the exact same amount to the destination `Budget` fact. Therefore the transfer preserves the total budget amount for the measure/version.
9. Source and destination currency must be compatible. Cross-currency transfers require a future explicit FX-aware transfer capability and are not silently converted.
10. The operation is audited and recalculates dependent formula measures for source and destination coordinates.
11. An approved transfer is not edited or deleted. A later correction should be implemented as a new reverse/corrective transfer so history remains append-only at the governance layer.

## Consequences
- The planning grid remains immutable for approved versions while authorized reallocations remain possible.
- Reports can separately expose original approval history and transfer activity.
- Transfer approval is concurrency-safe against simultaneous reservations or other transfers because source availability is checked again in the posting transaction.
- Future configurable approval matrices can replace the initial CFO/CEO/administrator rule without changing transfer accounting semantics.
