namespace PBM.Application;

public sealed record PurchaseDashboardMonthlyDto(
    Guid PeriodId,
    string PeriodName,
    int Sequence,
    decimal BudgetQuantity,
    decimal ForecastQuantity,
    decimal BudgetPurchaseAmount,
    decimal ForecastPurchaseAmount,
    decimal BudgetCostAmount,
    decimal ForecastCostAmount,
    decimal BudgetTotalAmount,
    decimal ForecastTotalAmount);

public sealed record PurchaseDashboardCostDto(
    Guid CostTypeId,
    string Code,
    string Name,
    decimal BudgetAmount,
    decimal ForecastAmount,
    decimal VarianceAmount);

public sealed record PurchaseDashboardDrilldownRowDto(
    Guid MemberId,
    string Code,
    string Name,
    decimal BudgetQuantity,
    decimal ForecastQuantity,
    decimal BudgetPurchaseAmount,
    decimal ForecastPurchaseAmount,
    decimal BudgetCostAmount,
    decimal ForecastCostAmount,
    decimal BudgetTotalAmount,
    decimal ForecastTotalAmount,
    decimal VarianceAmount);

public sealed record PurchaseDashboardDto(
    Guid VersionId,
    int VersionNumber,
    string VersionName,
    string CurrencyCode,
    decimal BudgetQuantity,
    decimal ForecastQuantity,
    decimal BudgetPurchaseAmount,
    decimal ForecastPurchaseAmount,
    decimal BudgetCostAmount,
    decimal ForecastCostAmount,
    decimal BudgetTotalAmount,
    decimal ForecastTotalAmount,
    decimal VarianceAmount,
    IReadOnlyList<PurchaseDashboardMonthlyDto> Monthly,
    IReadOnlyList<PurchaseDashboardCostDto> Costs,
    IReadOnlyList<DashboardDimensionOptionDto> Dimensions,
    Guid? SelectedDimensionId,
    IReadOnlyList<PurchaseDashboardDrilldownRowDto> Drilldown);

public interface IPurchaseDashboardService
{
    Task<PurchaseDashboardDto?> GetAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid? dimensionId = null,
        int take = 50,
        CancellationToken cancellationToken = default);
}
