# ADR-003 — Budget reservations and commitment accounting

## Status
Accepted

## Context
PBM must prevent operational users from creating obligations beyond an approved budget while keeping Actual values authoritative from accounting/ERP evidence. A reservation request is therefore not the same thing as an accounting Actual, and treating both as the same event would create double counting when ERP actuals are imported later.

## Decision
1. Reservations are created only against an approved/final budget version, an open fiscal period, an amount measure and a fully valid multidimensional budget coordinate.
2. `Available = Budget - Actual - Commitment` for the exact version/period/measure/coordinate.
3. A `Requested` reservation is a workflow request and does not yet change the Commitment fact.
4. Approval is executed in a serializable transaction, re-checks availability, and adds the approved amount to the `Commitment` fact for that coordinate.
5. Rejection does not change facts.
6. Release subtracts the reservation amount from Commitment.
7. Consume closes the open reservation and subtracts its amount from Commitment. It does **not** create an Actual fact.
8. Actual remains authoritative from a posted financial document, ERP/accounting connector or an explicitly governed Actual import. This avoids counting the same obligation once as reservation consumption and again when the financial transaction arrives.
9. Reservation-managed Commitment facts use `Source = ReservationLedger`. External Commitment imports must not overwrite reservation-ledger coordinates without an explicit reconciliation process.
10. Every reservation state transition is audited and produces an in-app notification for the relevant requester/reviewer population.

## Consequences
- Budget availability is protected at approval time even when multiple requests are pending concurrently.
- There can be a short period after a reservation is consumed and before the accounting Actual is imported where available budget appears higher. Operational reporting should expose unreconciled consumed reservations until ERP/Actual integration is completed.
- ERP integration must correlate reservation/external-reference identifiers with posted Actuals and provide a reconciliation report.
- Future partial consumption should be modeled explicitly instead of mutating the original approved amount silently.
