namespace PBM.Application;

public sealed record SalesDashboardMonthlyDto(
    Guid PeriodId,
    string PeriodName,
    int Sequence,
    decimal BudgetQuantity,
    decimal ForecastQuantity,
    decimal BudgetGrossSales,
    decimal ForecastGrossSales,
    decimal BudgetDiscount,
    decimal ForecastDiscount,
    decimal BudgetReturn,
    decimal ForecastReturn,
    decimal BudgetNetSales,
    decimal ForecastNetSales,
    decimal BudgetCogs,
    decimal ForecastCogs,
    decimal BudgetGrossProfit,
    decimal ForecastGrossProfit);

public sealed record SalesDashboardDrilldownRowDto(
    Guid MemberId,
    string Code,
    string Name,
    decimal BudgetQuantity,
    decimal ForecastQuantity,
    decimal BudgetGrossSales,
    decimal ForecastGrossSales,
    decimal BudgetNetSales,
    decimal ForecastNetSales,
    decimal BudgetCogs,
    decimal ForecastCogs,
    decimal BudgetGrossProfit,
    decimal ForecastGrossProfit,
    decimal NetSalesVariance);

public sealed record SalesDashboardDto(
    Guid VersionId,
    int VersionNumber,
    string VersionName,
    string CurrencyCode,
    decimal BudgetQuantity,
    decimal ForecastQuantity,
    decimal BudgetFreeQuantity,
    decimal ForecastFreeQuantity,
    decimal BudgetGrossSales,
    decimal ForecastGrossSales,
    decimal BudgetDiscount,
    decimal ForecastDiscount,
    decimal BudgetReturn,
    decimal ForecastReturn,
    decimal BudgetNetSales,
    decimal ForecastNetSales,
    decimal BudgetCogs,
    decimal ForecastCogs,
    decimal BudgetCompanyDiscount,
    decimal ForecastCompanyDiscount,
    decimal BudgetGrossProfit,
    decimal ForecastGrossProfit,
    decimal NetSalesVariance,
    IReadOnlyList<SalesDashboardMonthlyDto> Monthly,
    IReadOnlyList<DashboardDimensionOptionDto> Dimensions,
    Guid? SelectedDimensionId,
    IReadOnlyList<SalesDashboardDrilldownRowDto> Drilldown);

public interface ISalesDashboardService
{
    Task<SalesDashboardDto?> GetAsync(Guid companyId, Guid fiscalYearId, Guid? dimensionId = null, int take = 50, CancellationToken cancellationToken = default);
}
