using Microsoft.EntityFrameworkCore;
using PBM.Application;

namespace PBM.Infrastructure;

public sealed class PortfolioFinancialService(
    PbmDbContext db,
    IUserContext user,
    IFinancialReportService financialReports) : IPortfolioFinancialService
{
    private static readonly string[] RequiredCodes =
    [
        "NET_SALES", "GROSS_PROFIT", "OPERATING_PROFIT", "NET_PROFIT"
    ];

    public async Task<PortfolioFinancialPerformanceDto> GetAsync(
        Guid anchorCompanyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(anchorCompanyId, cancellationToken);
        var anchor = await db.FiscalYears.AsNoTracking()
            .Where(x => x.Id == fiscalYearId && x.CompanyId == anchorCompanyId)
            .Select(x => new { x.JalaliYear })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Fiscal year does not belong to the selected company.");

        var companyQuery = db.Companies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsActive);
        if (!user.IsInRole("SUPERADMIN"))
            companyQuery = companyQuery.Where(x => user.CompanyIds.Contains(x.Id));

        var companies = await companyQuery
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);
        var companyIds = companies.Select(x => x.Id).ToArray();
        var fiscalYears = await db.FiscalYears.AsNoTracking()
            .Where(x => companyIds.Contains(x.CompanyId) && x.JalaliYear == anchor.JalaliYear)
            .OrderByDescending(x => x.StartDate)
            .Select(x => new { x.Id, x.CompanyId, x.JalaliYear })
            .ToListAsync(cancellationToken);
        var fiscalYearByCompany = fiscalYears
            .GroupBy(x => x.CompanyId)
            .ToDictionary(x => x.Key, x => x.First());

        var rows = new List<PortfolioCompanyPerformanceDto>();
        foreach (var company in companies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!fiscalYearByCompany.TryGetValue(company.Id, out var year)) continue;

            var budget = await financialReports.GetAsync(
                company.Id, year.Id, FinancialReportType.ProfitLoss, Domain.ValueKind.Budget,
                cancellationToken: cancellationToken);
            var actual = await financialReports.GetAsync(
                company.Id, year.Id, FinancialReportType.ProfitLoss, Domain.ValueKind.Actual,
                cancellationToken: cancellationToken);
            var forecast = await financialReports.GetAsync(
                company.Id, year.Id, FinancialReportType.ProfitLoss, Domain.ValueKind.Forecast,
                cancellationToken: cancellationToken);

            var budgetValues = ToTotals(budget);
            var actualValues = ToTotals(actual);
            var forecastValues = ToTotals(forecast);
            var budgetSales = budgetValues["NET_SALES"];
            var actualSales = actualValues["NET_SALES"];
            var forecastSales = forecastValues["NET_SALES"];
            var budgetNetProfit = budgetValues["NET_PROFIT"];
            var actualNetProfit = actualValues["NET_PROFIT"];
            var forecastNetProfit = forecastValues["NET_PROFIT"];

            rows.Add(new PortfolioCompanyPerformanceDto(
                company.Id,
                company.Code,
                company.Name,
                year.Id,
                year.JalaliYear,
                budgetSales,
                actualSales,
                forecastSales,
                actualSales - budgetSales,
                forecastSales - budgetSales,
                budgetValues["GROSS_PROFIT"],
                actualValues["GROSS_PROFIT"],
                forecastValues["GROSS_PROFIT"],
                budgetValues["OPERATING_PROFIT"],
                actualValues["OPERATING_PROFIT"],
                forecastValues["OPERATING_PROFIT"],
                budgetNetProfit,
                actualNetProfit,
                forecastNetProfit,
                actualNetProfit - budgetNetProfit,
                forecastNetProfit - budgetNetProfit,
                Percent(actualNetProfit, actualSales),
                Percent(actualSales, budgetSales)));
        }

        rows = rows
            .OrderByDescending(x => x.ActualNetProfit)
            .ThenByDescending(x => x.ActualNetSales)
            .ThenBy(x => x.CompanyName)
            .ToList();

        var totalBudgetSales = rows.Sum(x => x.BudgetNetSales);
        var totalActualSales = rows.Sum(x => x.ActualNetSales);
        var totalForecastSales = rows.Sum(x => x.ForecastNetSales);
        var totalBudgetNetProfit = rows.Sum(x => x.BudgetNetProfit);
        var totalActualNetProfit = rows.Sum(x => x.ActualNetProfit);
        var totalForecastNetProfit = rows.Sum(x => x.ForecastNetProfit);
        var totals = new PortfolioFinancialTotalsDto(
            totalBudgetSales,
            totalActualSales,
            totalForecastSales,
            rows.Sum(x => x.BudgetGrossProfit),
            rows.Sum(x => x.ActualGrossProfit),
            rows.Sum(x => x.ForecastGrossProfit),
            rows.Sum(x => x.BudgetOperatingProfit),
            rows.Sum(x => x.ActualOperatingProfit),
            rows.Sum(x => x.ForecastOperatingProfit),
            totalBudgetNetProfit,
            totalActualNetProfit,
            totalForecastNetProfit,
            totalActualSales - totalBudgetSales,
            totalForecastSales - totalBudgetSales,
            totalActualNetProfit - totalBudgetNetProfit,
            totalForecastNetProfit - totalBudgetNetProfit,
            Percent(totalActualNetProfit, totalActualSales),
            Percent(totalActualSales, totalBudgetSales));

        var currency = await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsActive && x.IsBaseCurrency)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? "IRR";

        return new PortfolioFinancialPerformanceDto(
            anchor.JalaliYear,
            currency,
            companies.Count,
            rows.Count,
            totals,
            rows);
    }

    private static Dictionary<string, decimal> ToTotals(FinancialReportDto report)
    {
        var result = RequiredCodes.ToDictionary(x => x, _ => 0m, StringComparer.OrdinalIgnoreCase);
        foreach (var row in report.Rows)
            if (result.ContainsKey(row.Code)) result[row.Code] = row.Total;
        return result;
    }

    private static decimal Percent(decimal numerator, decimal denominator) =>
        denominator == 0 ? 0 : numerator / denominator * 100m;

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(
                x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive,
                cancellationToken))
            throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }
}
