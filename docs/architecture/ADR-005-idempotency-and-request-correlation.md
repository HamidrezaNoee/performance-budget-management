# ADR-005 — Request correlation and idempotent financial writes

## Status
Accepted

## Context
PBM receives interactive writes from the web application and will also receive retried requests from ERP, accounting, ETL and other integrations. Network timeouts can cause a caller to retry a request after the first attempt has already changed financial state. Blind retries can therefore duplicate reservations, transfers, Actual facts or other governed writes.

Operational troubleshooting also requires one stable identifier that can be returned to the caller and searched in structured logs.

## Decision

### Correlation ID
Authenticated `/api/v1` endpoints use a correlation filter. The API accepts `X-Correlation-ID` only when it is short and contains a restricted character set; otherwise PBM creates a trace identifier. The selected identifier is:

- stored in `HttpContext.Items` and `TraceIdentifier`;
- returned as `X-Correlation-ID`;
- included in structured logging scopes;
- returned in RFC 7807-style error payloads as `correlationId`.

### Idempotency key
JSON write endpoints support an optional `Idempotency-Key` header. Requests without the header behave normally. Multipart/file requests are excluded from the generic filter and require operation-specific deduplication if needed.

The request fingerprint includes method, path, query string and serializable endpoint arguments after model binding. Infrastructure/service arguments are excluded.

PBM persists an `IdempotencyRecord` for the authenticated tenant/user, endpoint scope and key. A SQL Server `sp_getapplock` transaction lock serializes first acquisition of a key across application instances without relying on process-local memory.

The default retention is seven days (`Idempotency:RetentionHours = 168`) and is configurable from 1 to 720 hours.

## At-most-once behavior
The generic guard intentionally chooses financial safety over transparent replay:

- first request: record becomes `Processing`, then the endpoint executes;
- success: record becomes `Completed`;
- duplicate completed request: PBM returns HTTP 409 and does not execute the endpoint again;
- same key with different payload: PBM returns HTTP 409 `IDEMPOTENCY_PAYLOAD_CONFLICT`;
- concurrent duplicate: PBM returns HTTP 409 `IDEMPOTENCY_IN_PROGRESS`;
- exception after acquisition: record becomes `Uncertain`; retries with the same key are blocked until business reconciliation is performed.

PBM does not automatically retry an `Uncertain` operation because the original request may have committed business data immediately before a transport or serialization failure. This prevents a second financial posting at the cost of requiring reconciliation in the rare ambiguous case.

## Consequences
- Integrations should generate a stable idempotency key per business command, not per HTTP attempt.
- UI calls do not need an idempotency key unless retry protection is desired.
- The new `IdempotencyRecord` entity must be included in the initial EF Core migration before Production deployment.
- Exact response replay is deliberately deferred. If future API consumers require replay semantics, a bounded response snapshot/outbox design can be added without changing business command keys.
- Correlation IDs must be propagated by external integrations when available, but untrusted arbitrary header content is never accepted without validation.
