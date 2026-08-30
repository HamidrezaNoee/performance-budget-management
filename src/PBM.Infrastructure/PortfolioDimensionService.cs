using Microsoft.EntityFrameworkCore;
using PBM.Application;

namespace PBM.Infrastructure;

public sealed class PortfolioDimensionService(
    PbmDbContext db,
    IUserContext user,
    CommercialPlanningProvisioner provisioner,
    ISalesDashboardService salesDashboard,
    IExpenseDashboardService expenseDashboard) : IPortfolioDimensionService
{
    private static readonly HashSet<string> SalesDimensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "PRODUCT", "SUPPLIER", "BRAND", "CUSTOMER", "REGION", "DEPARTMENT", "COSTCENTER",
        "CONTRACT", "ACCOUNT", "PROGRAM", "ACTIVITY", "PROJECT", "FUNDINGSOURCE", "CURRENCY"
    };

    private static readonly HashSet<string> ExpenseDimensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "DEPARTMENT", "ACCOUNT", "COSTCENTER", "EXPENSEITEM", "PROGRAM", "ACTIVITY", "PROJECT",
        "FUNDINGSOURCE", "CONTRACT", "REGION"
    };

    public async Task<PortfolioSalesDimensionRankingDto> GetSalesAsync(
        Guid anchorCompanyId,
        Guid fiscalYearId,
        string dimensionCode,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeDimensionCode(dimensionCode, SalesDimensions, "sales");
        var context = await ResolveContextAsync(anchorCompanyId, fiscalYearId, cancellationToken);
        await provisioner.EnsureSalesAsync(user.TenantId, cancellationToken);
        var dimension = await ResolveModelDimensionAsync("TRADE", code, cancellationToken);
        var contributions = new List<SalesContribution>();
        decimal totalActualNetSales = 0m;

        foreach (var company in context.Companies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.FiscalYearByCompany.TryGetValue(company.Id, out var yearId)) continue;
            var dashboard = await salesDashboard.GetAsync(company.Id, yearId, dimension.Id, 500, cancellationToken);
            if (dashboard is null) continue;
            totalActualNetSales += dashboard.ActualNetSales;
            foreach (var row in dashboard.Drilldown)
                contributions.Add(new SalesContribution(company.Id, row));
        }

        var rows = contributions
            .GroupBy(x => x.Row.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First().Row;
                var budgetSales = group.Sum(x => x.Row.BudgetNetSales);
                var actualSales = group.Sum(x => x.Row.ActualNetSales);
                var forecastSales = group.Sum(x => x.Row.ForecastNetSales);
                return new PortfolioSalesDimensionRowDto(
                    first.Code,
                    first.Name,
                    group.Select(x => x.CompanyId).Distinct().Count(),
                    budgetSales,
                    actualSales,
                    forecastSales,
                    actualSales - budgetSales,
                    forecastSales - budgetSales,
                    group.Sum(x => x.Row.BudgetGrossProfit),
                    group.Sum(x => x.Row.ActualGrossProfit),
                    group.Sum(x => x.Row.ForecastGrossProfit),
                    Percent(actualSales, budgetSales),
                    Percent(actualSales, totalActualNetSales));
            })
            .OrderByDescending(x => x.ActualNetSales)
            .ThenByDescending(x => x.ForecastNetSales)
            .ThenBy(x => x.MemberName)
            .Take(Math.Clamp(take, 1, 200))
            .ToList();

        return new PortfolioSalesDimensionRankingDto(
            context.JalaliYear,
            context.CurrencyCode,
            dimension.Code,
            dimension.Name,
            context.FiscalYearByCompany.Count,
            totalActualNetSales,
            rows);
    }

    public async Task<PortfolioExpenseDimensionRankingDto> GetExpensesAsync(
        Guid anchorCompanyId,
        Guid fiscalYearId,
        string dimensionCode,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeDimensionCode(dimensionCode, ExpenseDimensions, "expense");
        var context = await ResolveContextAsync(anchorCompanyId, fiscalYearId, cancellationToken);
        await provisioner.EnsureExpenseAsync(user.TenantId, cancellationToken);
        var dimension = await ResolveModelDimensionAsync("EXPENSE", code, cancellationToken);
        var contributions = new List<ExpenseContribution>();
        decimal totalActualNetCost = 0m;

        foreach (var company in context.Companies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.FiscalYearByCompany.TryGetValue(company.Id, out var yearId)) continue;
            var dashboard = await expenseDashboard.GetAsync(company.Id, yearId, dimension.Id, 500, cancellationToken);
            if (dashboard is null) continue;
            totalActualNetCost += dashboard.ActualNetCost;
            foreach (var row in dashboard.Drilldown)
                contributions.Add(new ExpenseContribution(company.Id, row));
        }

        var rows = contributions
            .GroupBy(x => x.Row.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First().Row;
                var budget = group.Sum(x => x.Row.BudgetNetCost);
                var actual = group.Sum(x => x.Row.ActualNetCost);
                var forecast = group.Sum(x => x.Row.ForecastNetCost);
                return new PortfolioExpenseDimensionRowDto(
                    first.Code,
                    first.Name,
                    group.Select(x => x.CompanyId).Distinct().Count(),
                    budget,
                    actual,
                    forecast,
                    actual - budget,
                    forecast - budget,
                    Percent(actual, budget),
                    Percent(actual, totalActualNetCost));
            })
            .OrderByDescending(x => x.ActualNetCost)
            .ThenByDescending(x => x.ForecastNetCost)
            .ThenBy(x => x.MemberName)
            .Take(Math.Clamp(take, 1, 200))
            .ToList();

        return new PortfolioExpenseDimensionRankingDto(
            context.JalaliYear,
            context.CurrencyCode,
            dimension.Code,
            dimension.Name,
            context.FiscalYearByCompany.Count,
            totalActualNetCost,
            rows);
    }

    private async Task<PortfolioContext> ResolveContextAsync(
        Guid anchorCompanyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken)
    {
        await EnsureCompanyAsync(anchorCompanyId, cancellationToken);
        var jalaliYear = await db.FiscalYears.AsNoTracking()
            .Where(x => x.Id == fiscalYearId && x.CompanyId == anchorCompanyId)
            .Select(x => (int?)x.JalaliYear)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Fiscal year does not belong to the selected company.");

        var companyQuery = db.Companies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsActive);
        if (!user.IsInRole("SUPERADMIN"))
            companyQuery = companyQuery.Where(x => user.CompanyIds.Contains(x.Id));
        var companies = await companyQuery
            .OrderBy(x => x.Name)
            .Select(x => new CompanyRef(x.Id, x.Code, x.Name))
            .ToListAsync(cancellationToken);
        var companyIds = companies.Select(x => x.Id).ToArray();
        var fiscalYears = await db.FiscalYears.AsNoTracking()
            .Where(x => companyIds.Contains(x.CompanyId) && x.JalaliYear == jalaliYear)
            .OrderByDescending(x => x.StartDate)
            .Select(x => new { x.CompanyId, x.Id })
            .ToListAsync(cancellationToken);
        var fiscalYearByCompany = fiscalYears.GroupBy(x => x.CompanyId)
            .ToDictionary(x => x.Key, x => x.First().Id);
        var currency = await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsActive && x.IsBaseCurrency)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? "IRR";
        return new PortfolioContext(jalaliYear, currency, companies, fiscalYearByCompany);
    }

    private async Task<DimensionRef> ResolveModelDimensionAsync(
        string modelCode,
        string dimensionCode,
        CancellationToken cancellationToken)
    {
        return await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModel!.TenantId == user.TenantId
                && x.BudgetModel.Code == modelCode
                && x.BudgetModel.IsActive
                && x.Dimension!.Code == dimensionCode
                && x.Dimension.IsActive)
            .Select(x => new DimensionRef(x.DimensionId, x.Dimension!.Code, x.Dimension.Name))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException($"Dimension '{dimensionCode}' is not attached to the {modelCode} model.");
    }

    private static string NormalizeDimensionCode(string value, IReadOnlySet<string> allowed, string scope)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (!allowed.Contains(code))
            throw new ArgumentException($"Dimension '{code}' is not supported for portfolio {scope} ranking.");
        return code;
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

    private sealed record CompanyRef(Guid Id, string Code, string Name);
    private sealed record DimensionRef(Guid Id, string Code, string Name);
    private sealed record PortfolioContext(
        int JalaliYear,
        string CurrencyCode,
        IReadOnlyList<CompanyRef> Companies,
        IReadOnlyDictionary<Guid, Guid> FiscalYearByCompany);
    private sealed record SalesContribution(Guid CompanyId, SalesDashboardDrilldownRowDto Row);
    private sealed record ExpenseContribution(Guid CompanyId, ExpenseDashboardDrilldownRowDto Row);
}
