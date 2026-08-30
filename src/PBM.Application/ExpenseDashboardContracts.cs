namespace PBM.Application;

public sealed record ExpenseDashboardMonthlyDto(
    Guid PeriodId,
    string PeriodName,
    int Sequence,
    decimal BudgetExpense,
    decimal ForecastExpense,
    decimal BudgetIncome,
    decimal ForecastIncome,
    decimal BudgetNetCost,
    decimal ForecastNetCost);

public sealed record ExpenseDashboardClassRowDto(
    Guid MemberId,
    string Code,
    string Name,
    decimal BudgetAmount,
    decimal ForecastAmount,
    decimal VarianceAmount);

public sealed record ExpenseDashboardDrilldownRowDto(
    Guid MemberId,
    string Code,
    string Name,
    decimal BudgetExpense,
    decimal ForecastExpense,
    decimal BudgetIncome,
    decimal ForecastIncome,
    decimal BudgetNetCost,
    decimal ForecastNetCost,
    decimal VarianceAmount);

public sealed record ExpenseDashboardDto(
    Guid VersionId,
    int VersionNumber,
    string VersionName,
    string CurrencyCode,
    decimal BudgetExpense,
    decimal ForecastExpense,
    decimal BudgetIncome,
    decimal ForecastIncome,
    decimal BudgetNetCost,
    decimal ForecastNetCost,
    decimal VarianceAmount,
    IReadOnlyList<ExpenseDashboardMonthlyDto> Monthly,
    IReadOnlyList<ExpenseDashboardClassRowDto> Classes,
    IReadOnlyList<DashboardDimensionOptionDto> Dimensions,
    Guid? SelectedDimensionId,
    IReadOnlyList<ExpenseDashboardDrilldownRowDto> Drilldown);

public interface IExpenseDashboardService
{
    Task<ExpenseDashboardDto?> GetAsync(Guid companyId, Guid fiscalYearId, Guid? dimensionId = null, int take = 50, CancellationToken cancellationToken = default);
}
