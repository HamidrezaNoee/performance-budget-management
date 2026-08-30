namespace PBM.Application;

public sealed record PortfolioCompanyPerformanceDto(
    Guid CompanyId,
    string CompanyCode,
    string CompanyName,
    Guid FiscalYearId,
    int JalaliYear,
    decimal BudgetNetSales,
    decimal ActualNetSales,
    decimal ForecastNetSales,
    decimal ActualNetSalesVariance,
    decimal ForecastNetSalesVariance,
    decimal BudgetGrossProfit,
    decimal ActualGrossProfit,
    decimal ForecastGrossProfit,
    decimal BudgetOperatingProfit,
    decimal ActualOperatingProfit,
    decimal ForecastOperatingProfit,
    decimal BudgetNetProfit,
    decimal ActualNetProfit,
    decimal ForecastNetProfit,
    decimal ActualNetProfitVariance,
    decimal ForecastNetProfitVariance,
    decimal ActualNetMarginPercent,
    decimal BudgetAchievementPercent);

public sealed record PortfolioFinancialTotalsDto(
    decimal BudgetNetSales,
    decimal ActualNetSales,
    decimal ForecastNetSales,
    decimal BudgetGrossProfit,
    decimal ActualGrossProfit,
    decimal ForecastGrossProfit,
    decimal BudgetOperatingProfit,
    decimal ActualOperatingProfit,
    decimal ForecastOperatingProfit,
    decimal BudgetNetProfit,
    decimal ActualNetProfit,
    decimal ForecastNetProfit,
    decimal ActualNetSalesVariance,
    decimal ForecastNetSalesVariance,
    decimal ActualNetProfitVariance,
    decimal ForecastNetProfitVariance,
    decimal ActualNetMarginPercent,
    decimal BudgetAchievementPercent);

public sealed record PortfolioFinancialPerformanceDto(
    int JalaliYear,
    string CurrencyCode,
    int AccessibleCompanyCount,
    int CompaniesWithFiscalYear,
    PortfolioFinancialTotalsDto Totals,
    IReadOnlyList<PortfolioCompanyPerformanceDto> Companies);

public interface IPortfolioFinancialService
{
    Task<PortfolioFinancialPerformanceDto> GetAsync(
        Guid anchorCompanyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default);
}
