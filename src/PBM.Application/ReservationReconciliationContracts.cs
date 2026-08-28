namespace PBM.Application;

public enum ReservationReconciliationStatus
{
    AwaitingActual,
    MissingActual,
    Reconciled,
    UnderPosted,
    OverPosted,
    CurrencyConflict
}

public sealed record ReservationReconciliationDecision(
    ReservationReconciliationStatus Status,
    decimal ActualAmount,
    decimal Variance,
    decimal AllowedTolerance);

public static class ReservationReconciliationPolicy
{
    public static ReservationReconciliationDecision Evaluate(
        decimal consumedAmount,
        decimal? actualAmount,
        int daysSinceFirstConsumption,
        int graceDays,
        decimal tolerancePercent,
        bool currencyConflict)
    {
        if (consumedAmount < 0m) throw new ArgumentOutOfRangeException(nameof(consumedAmount));
        if (daysSinceFirstConsumption < 0) daysSinceFirstConsumption = 0;
        if (graceDays is < 0 or > 365) throw new ArgumentOutOfRangeException(nameof(graceDays));
        if (tolerancePercent is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(tolerancePercent));

        var actual = actualAmount ?? 0m;
        var variance = actual - consumedAmount;
        var tolerance = Math.Max(0.01m, Math.Abs(consumedAmount) * tolerancePercent / 100m);

        if (currencyConflict)
            return new(ReservationReconciliationStatus.CurrencyConflict, actual, variance, tolerance);
        if (!actualAmount.HasValue)
            return new(daysSinceFirstConsumption <= graceDays
                    ? ReservationReconciliationStatus.AwaitingActual
                    : ReservationReconciliationStatus.MissingActual,
                actual, variance, tolerance);
        if (Math.Abs(variance) <= tolerance)
            return new(ReservationReconciliationStatus.Reconciled, actual, variance, tolerance);
        return new(variance < 0m ? ReservationReconciliationStatus.UnderPosted : ReservationReconciliationStatus.OverPosted,
            actual, variance, tolerance);
    }
}

public sealed record ReservationReconciliationItemDto(
    Guid CompanyId,
    Guid FiscalYearId,
    Guid VersionId,
    int VersionNumber,
    Guid PeriodId,
    string PeriodName,
    Guid MeasureId,
    string MeasureCode,
    string MeasureName,
    string CoordinateHash,
    string CurrencyCode,
    IReadOnlyList<string> ReservationNumbers,
    IReadOnlyList<string> ExternalReferences,
    int ReservationCount,
    decimal ConsumedAmount,
    decimal ActualAmount,
    decimal Variance,
    decimal AllowedTolerance,
    DateTime FirstConsumedAtUtc,
    DateTime LastConsumedAtUtc,
    int DaysSinceFirstConsumption,
    ReservationReconciliationStatus Status,
    Guid? ActualFactId,
    string? ActualSource,
    DateTime? ActualUpdatedAtUtc,
    IReadOnlyList<DimensionSelection> Dimensions);

public sealed record ReservationReconciliationCurrencySummaryDto(
    string CurrencyCode,
    int CoordinateCount,
    int ReconciledCount,
    int OpenIssueCount,
    decimal ConsumedAmount,
    decimal ActualAmount,
    decimal Variance);

public sealed record ReservationReconciliationSummaryDto(
    Guid CompanyId,
    Guid? FiscalYearId,
    int GraceDays,
    decimal TolerancePercent,
    int CoordinateCount,
    int ReconciledCount,
    int AwaitingCount,
    int MissingCount,
    int UnderPostedCount,
    int OverPostedCount,
    int CurrencyConflictCount,
    IReadOnlyList<ReservationReconciliationCurrencySummaryDto> Currencies,
    IReadOnlyList<ReservationReconciliationItemDto> Items);

public interface IReservationReconciliationService
{
    Task<ReservationReconciliationSummaryDto> GetAsync(
        Guid companyId,
        Guid? fiscalYearId = null,
        int graceDays = 2,
        decimal tolerancePercent = 0.1m,
        CancellationToken cancellationToken = default);
}
