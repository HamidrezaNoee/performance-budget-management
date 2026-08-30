namespace PBM.Application;

public sealed record ExpenseDashboardMonthlyDto(
    Guid PeriodId,
    string PeriodName,
    int Sequence,
    decimal BudgetExpense,
    decimal ActualExpense,
    decimal ForecastExpense,
    decimal BudgetIncome,
    decimal ActualIncome,
    decimal ForecastIncome,
    decimal BudgetNetCost,
    decimal ActualNetCost,
    decimal ForecastNetCost);

public sealed record ExpenseDashboardClassRowDto(
    Guid MemberId,
    string Code,
    string Name,
    decimal BudgetAmount,
    decimal ActualAmount,
    decimal ForecastAmount,
    decimal ActualVarianceAmount,
    decimal ForecastVarianceAmount);

public sealed record ExpenseDashboardDrilldownRowDto(
    Guid MemberId,
    string Code,
    string Name,
    decimal BudgetExpense,
    decimal ActualExpense,
    decimal ForecastExpense,
    decimal BudgetIncome,
    decimal ActualIncome,
    decimal ForecastIncome,
    decimal BudgetNetCost,
    decimal ActualNetCost,
    decimal ForecastNetCost,
    decimal ActualVarianceAmount,
    decimal ForecastVarianceAmount);

public sealed record ExpenseDashboardDto(
    Guid VersionId,
    int VersionNumber,
    string VersionName,
    string CurrencyCode,
    decimal BudgetExpense,
    decimal ActualExpense,
    decimal ForecastExpense,
    decimal BudgetIncome,
    decimal ActualIncome,
    decimal ForecastIncome,
    decimal BudgetNetCost,
    decimal ActualNetCost,
    decimal ForecastNetCost,
    decimal ActualVarianceAmount,
    decimal ForecastVarianceAmount,
    IReadOnlyList<ExpenseDashboardMonthlyDto> Monthly,
    IReadOnlyList<ExpenseDashboardClassRowDto> Classes,
    IReadOnlyList<DashboardDimensionOptionDto> Dimensions,
    Guid? SelectedDimensionId,
    IReadOnlyList<ExpenseDashboardDrilldownRowDto> Drilldown);

public interface IExpenseDashboardService
{
    Task<ExpenseDashboardDto?> GetAsync(Guid companyId, Guid fiscalYearId, Guid? dimensionId = null, int take = 50, CancellationToken cancellationToken = default);
}
