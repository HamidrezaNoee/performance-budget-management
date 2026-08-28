using PBM.Domain;

namespace PBM.Application;

public sealed record PostActualLedgerRequest(
    Guid VersionId,
    Guid PeriodId,
    Guid MeasureId,
    DateTime PostingDate,
    decimal Amount,
    string CurrencyCode,
    IReadOnlyList<DimensionSelection> Dimensions,
    string SourceSystem,
    string ExternalDocumentId,
    string ExternalLineId,
    string? Note);

public sealed record ReverseActualLedgerRequest(string Reason);

public sealed record ActualLedgerEntryDto(
    Guid Id,
    ActualLedgerEntryType EntryType,
    Guid? OriginalEntryId,
    Guid CompanyId,
    Guid VersionId,
    Guid PeriodId,
    Guid MeasureId,
    string SourceSystem,
    string ExternalDocumentId,
    string ExternalLineId,
    DateTime PostingDate,
    decimal Amount,
    string CurrencyCode,
    string CoordinateHash,
    string? Note,
    string? ReversalReason,
    bool IsReversed,
    DateTime CreatedAtUtc);

public sealed record ActualLedgerPostResult(
    ActualLedgerEntryDto Entry,
    bool WasDuplicate,
    Guid ProjectionFactId,
    decimal ProjectedActual);

public enum ActualLedgerReconciliationStatus
{
    Reconciled,
    MissingProjection,
    AmountMismatch,
    CurrencyMismatch,
    ProjectionWithoutLedger
}

public sealed record ActualLedgerReconciliationDto(
    Guid VersionId,
    Guid PeriodId,
    Guid MeasureId,
    string CoordinateHash,
    string CurrencyCode,
    decimal LedgerAmount,
    decimal? ProjectedAmount,
    string? ProjectedCurrencyCode,
    ActualLedgerReconciliationStatus Status,
    decimal Difference);

public interface IActualLedgerService
{
    Task<ActualLedgerPostResult> PostAsync(
        PostActualLedgerRequest request,
        CancellationToken cancellationToken = default);

    Task<ActualLedgerPostResult> ReverseAsync(
        Guid entryId,
        ReverseActualLedgerRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActualLedgerEntryDto>> GetEntriesAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid? versionId = null,
        string? sourceSystem = null,
        int take = 500,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActualLedgerReconciliationDto>> ReconcileAsync(
        Guid versionId,
        decimal tolerance = 0.01m,
        CancellationToken cancellationToken = default);

    Task<int> RebuildProjectionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);
}
