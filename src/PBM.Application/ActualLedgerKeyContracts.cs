namespace PBM.Application;

public sealed record PostActualLedgerByKeyRequest(
    Guid VersionId,
    string PeriodCode,
    string MeasureCode,
    DateTime PostingDate,
    decimal Amount,
    string CurrencyCode,
    IReadOnlyDictionary<string, string> Dimensions,
    string SourceSystem,
    string ExternalDocumentId,
    string ExternalLineId,
    string? Note);

public interface IActualLedgerKeyPostingService
{
    Task<ActualLedgerPostResult> PostAsync(
        PostActualLedgerByKeyRequest request,
        CancellationToken cancellationToken = default);
}
