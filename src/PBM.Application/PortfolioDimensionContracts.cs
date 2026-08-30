namespace PBM.Application;

public sealed record PortfolioSalesDimensionRowDto(
    string MemberCode,
    string MemberName,
    int CompanyCount,
    decimal BudgetNetSales,
    decimal ActualNetSales,
    decimal ForecastNetSales,
    decimal ActualNetSalesVariance,
    decimal ForecastNetSalesVariance,
    decimal BudgetGrossProfit,
    decimal ActualGrossProfit,
    decimal ForecastGrossProfit,
    decimal BudgetAchievementPercent,
    decimal ActualContributionPercent);

public sealed record PortfolioSalesDimensionRankingDto(
    int JalaliYear,
    string CurrencyCode,
    string DimensionCode,
    string DimensionName,
    int CompaniesWithFiscalYear,
    decimal TotalActualNetSales,
    IReadOnlyList<PortfolioSalesDimensionRowDto> Rows);

public sealed record PortfolioExpenseDimensionRowDto(
    string MemberCode,
    string MemberName,
    int CompanyCount,
    decimal BudgetNetCost,
    decimal ActualNetCost,
    decimal ForecastNetCost,
    decimal ActualVarianceAmount,
    decimal ForecastVarianceAmount,
    decimal BudgetAchievementPercent,
    decimal ActualContributionPercent);

public sealed record PortfolioExpenseDimensionRankingDto(
    int JalaliYear,
    string CurrencyCode,
    string DimensionCode,
    string DimensionName,
    int CompaniesWithFiscalYear,
    decimal TotalActualNetCost,
    IReadOnlyList<PortfolioExpenseDimensionRowDto> Rows);

public interface IPortfolioDimensionService
{
    Task<PortfolioSalesDimensionRankingDto> GetSalesAsync(
        Guid anchorCompanyId,
        Guid fiscalYearId,
        string dimensionCode,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<PortfolioExpenseDimensionRankingDto> GetExpensesAsync(
        Guid anchorCompanyId,
        Guid fiscalYearId,
        string dimensionCode,
        int take = 50,
        CancellationToken cancellationToken = default);
}
