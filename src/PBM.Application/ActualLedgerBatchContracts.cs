namespace PBM.Application;

public sealed record PostActualLedgerBatchRequest(
    IReadOnlyList<PostActualLedgerByKeyRequest> Entries,
    bool ContinueOnError = true);

public sealed record ActualLedgerBatchItemResult(
    int Index,
    string SourceSystem,
    string ExternalDocumentId,
    string ExternalLineId,
    bool Success,
    bool WasDuplicate,
    Guid? EntryId,
    decimal? ProjectedActual,
    string? Error);

public sealed record ActualLedgerBatchResult(
    int Total,
    int Succeeded,
    int Duplicates,
    int Failed,
    IReadOnlyList<ActualLedgerBatchItemResult> Items);

public interface IActualLedgerBatchService
{
    Task<ActualLedgerBatchResult> PostAsync(
        PostActualLedgerBatchRequest request,
        CancellationToken cancellationToken = default);
}
