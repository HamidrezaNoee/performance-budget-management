namespace PBM.Application;

public sealed record DashboardMetricOptionDto(
    string Code,
    string Name,
    string? Unit,
    string CurrencyCode,
    int DisplayOrder);

public sealed record DashboardDimensionOptionDto(
    Guid Id,
    string Code,
    string Name,
    int Sequence);

public sealed record DashboardMeasureSummaryDto(
    string MeasureCode,
    string MeasureName,
    string? Unit,
    string CurrencyCode,
    DashboardSummaryDto Summary);

public sealed record DashboardDrilldownRowDto(
    Guid MemberId,
    string Code,
    string Name,
    decimal Budget,
    decimal Actual,
    decimal Commitment,
    decimal Forecast,
    decimal Remaining,
    decimal Variance,
    decimal BudgetUtilizationPercent);

public sealed record DashboardDrilldownDto(
    Guid DimensionId,
    string DimensionCode,
    string DimensionName,
    string MeasureCode,
    string MeasureName,
    string? Unit,
    string CurrencyCode,
    int TotalMemberCount,
    IReadOnlyList<DashboardDrilldownRowDto> Rows);

public interface IDashboardAnalyticsService
{
    Task<IReadOnlyList<DashboardMetricOptionDto>> GetMetricOptionsAsync(
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<DashboardMeasureSummaryDto> GetSummaryForMeasureAsync(
        Guid companyId,
        Guid fiscalYearId,
        string measureCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardDimensionOptionDto>> GetDrilldownDimensionsAsync(
        Guid companyId,
        Guid fiscalYearId,
        string measureCode,
        CancellationToken cancellationToken = default);

    Task<DashboardDrilldownDto> GetDrilldownAsync(
        Guid companyId,
        Guid fiscalYearId,
        string measureCode,
        Guid dimensionId,
        int take = 50,
        CancellationToken cancellationToken = default);
}
