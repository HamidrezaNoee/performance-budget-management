namespace PBM.Application;

public enum PerformanceFundingRecommendation
{
    InsufficientData,
    MaintainFunding,
    PriorityForIncrement,
    MonitorClosely,
    ReviewFunding,
    CorrectiveAction
}

public sealed record PerformanceBudgetCurrencyDto(
    string CurrencyCode,
    decimal AnnualBudget,
    decimal AnnualActual,
    decimal AnnualCommitment,
    decimal AnnualForecast,
    decimal YtdBudget,
    decimal YtdActual,
    decimal YtdCommitment,
    decimal YtdForecast,
    decimal YtdUtilizationPercent,
    decimal YtdExposurePercent,
    decimal AnnualForecastPercent);

public sealed record PerformanceKpiComponentDto(
    Guid KpiId,
    string Code,
    string Name,
    string ScoreMode,
    decimal Weight,
    int ObservationCount,
    decimal AverageScore,
    decimal LatestScore,
    bool LatestIsOnTarget);

public sealed record PerformanceBudgetScorecardDto(
    Guid VersionId,
    Guid CompanyId,
    Guid FiscalYearId,
    Guid BudgetModelId,
    string VersionName,
    int VersionNumber,
    string ScenarioName,
    string FiscalYearName,
    string SelectedMeasureCode,
    string SelectedMeasureName,
    int ElapsedPeriods,
    int TotalPeriods,
    decimal DataCoveragePercent,
    decimal? WeightedKpiScore,
    PerformanceFundingRecommendation Recommendation,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<PerformanceBudgetCurrencyDto> Currencies,
    IReadOnlyList<PerformanceKpiComponentDto> Kpis);

public interface IPerformanceBudgetingService
{
    Task<PerformanceBudgetScorecardDto> GetScorecardAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);
}
