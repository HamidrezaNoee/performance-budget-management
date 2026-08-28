# ERP / Accounting Actual integration

PBM accepts execution data through an immutable Actual Ledger. The recommended integration endpoint is the external-key API because the ERP does not need PBM member GUIDs.

## Recommended endpoint

`POST /api/v1/actual-ledger/post-by-key`

Use the normal PBM bearer token. For retryable commands also send an `Idempotency-Key` header. The accounting business key is still enforced independently from the HTTP idempotency key.

Example payload:

```json
{
  "versionId": "11111111-1111-1111-1111-111111111111",
  "periodCode": "1405-01",
  "measureCode": "OPEX_AMOUNT",
  "postingDate": "2026-03-28T00:00:00Z",
  "amount": 1250000000,
  "currencyCode": "IRR",
  "dimensions": {
    "ACCOUNT": "610101",
    "COST_CENTER": "FINANCE",
    "DEPARTMENT": "FIN",
    "PROJECT": "ERP-2026"
  },
  "sourceSystem": "ERP",
  "externalDocumentId": "JV-1405-000184",
  "externalLineId": "3",
  "note": "General-ledger posting"
}
```

For each dimension value PBM first resolves `DimensionMember.ExternalKey`; the PBM member `Code` is also accepted. Required model dimensions must all be supplied.

## Response semantics

A successful first posting returns `wasDuplicate=false`, the immutable ledger entry and the resulting aggregated Actual projection.

Repeating the same SourceSystem + document + line with the same payload returns success with `wasDuplicate=true`. It does not create a second accounting row.

Repeating that business identity with a changed amount, currency, coordinate, period, measure or other canonical payload is rejected. Do not overwrite the old source row. Reverse it and send the corrected source line with a new ERP revision/line identity.

## Batch ingestion

`POST /api/v1/actual-ledger/batch`

```json
{
  "continueOnError": true,
  "entries": [
    {
      "versionId": "11111111-1111-1111-1111-111111111111",
      "periodCode": "1405-01",
      "measureCode": "OPEX_AMOUNT",
      "postingDate": "2026-03-28T00:00:00Z",
      "amount": 1250000000,
      "currencyCode": "IRR",
      "dimensions": {
        "ACCOUNT": "610101",
        "COST_CENTER": "FINANCE"
      },
      "sourceSystem": "ERP",
      "externalDocumentId": "JV-1405-000184",
      "externalLineId": "3",
      "note": null
    }
  ]
}
```

The maximum batch size is 1000 rows. Rows are committed independently. When `continueOnError=true`, the response contains success/error state for every row. This is intentional: after a network failure the producer can safely resend the complete batch and previously committed rows will be returned as duplicates.

## Reversal

`POST /api/v1/actual-ledger/{entryId}/reverse`

```json
{
  "reason": "ERP journal was cancelled by document JV-1405-000231"
}
```

Reversal never updates or deletes the original row. PBM writes an immutable negative row and immediately recalculates the Actual projection. A closed fiscal period cannot be reversed in place; send a governed adjustment to an open period instead.

## Reconciliation

`GET /api/v1/actual-ledger/reconciliation?versionId=<guid>&tolerance=0.01`

The endpoint compares ledger totals with ledger-owned `BudgetFact.Actual` projections. The UI exposes the same information under **Actual و اتصال ERP**.

Authorized finance/admin users can repair projection drift with:

`POST /api/v1/actual-ledger/rebuild-projection?versionId=<guid>`

Rebuild never changes immutable ledger rows.

## Integration rules

1. Keep `SourceSystem` stable for the lifetime of the connector.
2. Make `ExternalDocumentId + ExternalLineId` stable and unique inside that source.
3. Use `ExternalKey` on PBM dimension members for ERP account, cost-center, product, supplier, project and similar keys.
4. Do not reuse an external business key for changed payloads.
5. Use reversal/correction semantics rather than DELETE or UPDATE.
6. Do not post two currencies into the same PBM business coordinate unless currency is modeled as a dimension.
7. Send the same `Idempotency-Key` when retrying the exact same HTTP command after an uncertain network result.
8. Keep the returned PBM Correlation ID in integration logs for support/reconciliation.
