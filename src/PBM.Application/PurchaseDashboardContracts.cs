namespace PBM.Application;

public sealed record PurchaseDashboardMonthlyDto(
    Guid PeriodId,
    string PeriodName,
    int Sequence,
    decimal BudgetQuantity,
    decimal ActualQuantity,
    decimal ForecastQuantity,
    decimal BudgetPurchaseAmount,
    decimal ActualPurchaseAmount,
    decimal ForecastPurchaseAmount,
    decimal BudgetCostAmount,
    decimal ActualCostAmount,
    decimal ForecastCostAmount,
    decimal BudgetTotalAmount,
    decimal ActualTotalAmount,
    decimal ForecastTotalAmount);

public sealed record PurchaseDashboardCostDto(
    Guid CostTypeId,
    string Code,
    string Name,
    decimal BudgetAmount,
    decimal ActualAmount,
    decimal ForecastAmount,
    decimal ActualVarianceAmount,
    decimal ForecastVarianceAmount);

public sealed record PurchaseDashboardDrilldownRowDto(
    Guid MemberId,
    string Code,
    string Name,
    decimal BudgetQuantity,
    decimal ActualQuantity,
    decimal ForecastQuantity,
    decimal BudgetPurchaseAmount,
    decimal ActualPurchaseAmount,
    decimal ForecastPurchaseAmount,
    decimal BudgetCostAmount,
    decimal ActualCostAmount,
    decimal ForecastCostAmount,
    decimal BudgetTotalAmount,
    decimal ActualTotalAmount,
    decimal ForecastTotalAmount,
    decimal ActualVarianceAmount,
    decimal ForecastVarianceAmount);

public sealed record PurchaseDashboardDto(
    Guid VersionId,
    int VersionNumber,
    string VersionName,
    string CurrencyCode,
    decimal BudgetQuantity,
    decimal ActualQuantity,
    decimal ForecastQuantity,
    decimal BudgetPurchaseAmount,
    decimal ActualPurchaseAmount,
    decimal ForecastPurchaseAmount,
    decimal BudgetCostAmount,
    decimal ActualCostAmount,
    decimal ForecastCostAmount,
    decimal BudgetTotalAmount,
    decimal ActualTotalAmount,
    decimal ForecastTotalAmount,
    decimal ActualVarianceAmount,
    decimal ForecastVarianceAmount,
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
