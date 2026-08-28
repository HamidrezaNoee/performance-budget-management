using PBM.Domain;

namespace PBM.Application;

public sealed record VarianceAnalysisQuery(
    Guid CompanyId,
    Guid FiscalYearId,
    Guid BudgetModelId,
    Guid MeasureId,
    Guid RowDimensionId,
    IReadOnlyList<DimensionSelection> Filters,
    int Take = 20);

public sealed record VarianceAnalysisItemDto(
    Guid MemberId,
    string MemberCode,
    string MemberName,
    decimal Budget,
    decimal Actual,
    decimal Commitment,
    decimal Forecast,
    decimal Variance,
    decimal? VariancePercent,
    decimal? AchievementPercent);

public sealed record VarianceAnalysisDto(
    Guid VersionId,
    int VersionNumber,
    MeasureDto Measure,
    DimensionDto RowDimension,
    decimal TotalBudget,
    decimal TotalActual,
    decimal TotalCommitment,
    decimal TotalForecast,
    IReadOnlyList<VarianceAnalysisItemDto> Items);

public interface IVarianceAnalysisService
{
    Task<VarianceAnalysisDto> AnalyzeAsync(VarianceAnalysisQuery query, CancellationToken cancellationToken = default);
}
