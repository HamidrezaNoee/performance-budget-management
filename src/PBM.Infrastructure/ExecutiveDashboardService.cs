using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ExecutiveDashboardService(PbmDbContext db, IUserContext user) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, cancellationToken);
        if (!await db.FiscalYears.AnyAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");

        var baseCurrency = await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsBaseCurrency && x.IsActive)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? "IRR";

        var versionRefs = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.CompanyId == companyId && x.BudgetPlan.FiscalYearId == fiscalYearId)
            .Select(x => new { x.Id, x.BudgetPlanId, x.VersionNumber, x.Status })
            .ToListAsync(cancellationToken);
        var latestVersionIds = versionRefs
            .Where(x => x.Status != BudgetStatus.Rejected)
            .GroupBy(x => x.BudgetPlanId)
            .Select(g => g.OrderByDescending(x => x.VersionNumber).First().Id)
            .ToArray();

        var query = db.BudgetFacts.AsNoTracking().Where(x =>
            latestVersionIds.Contains(x.VersionId)
            && x.Measure!.ValueType == MeasureValueType.Amount
            && x.CurrencyCode == baseCurrency);

        var totals = await query.GroupBy(_ => 1).Select(g => new
        {
            Budget = g.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value),
            Actual = g.Where(x => x.ValueKind == ValueKind.Actual).Sum(x => x.Value),
            Commitment = g.Where(x => x.ValueKind == ValueKind.Commitment).Sum(x => x.Value),
            Forecast = g.Where(x => x.ValueKind == ValueKind.Forecast).Sum(x => x.Value)
        }).SingleOrDefaultAsync(cancellationToken);

        var grouped = await query.GroupBy(x => x.PeriodId).Select(g => new
        {
            PeriodId = g.Key,
            Budget = g.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value),
            Actual = g.Where(x => x.ValueKind == ValueKind.Actual).Sum(x => x.Value),
            Commitment = g.Where(x => x.ValueKind == ValueKind.Commitment).Sum(x => x.Value),
            Forecast = g.Where(x => x.ValueKind == ValueKind.Forecast).Sum(x => x.Value)
        }).ToDictionaryAsync(x => x.PeriodId, cancellationToken);

        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYearId).OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        var monthly = periods.Select(period => grouped.TryGetValue(period.Id, out var value)
            ? new MonthlySeriesPointDto(period.Id, period.Name, period.Sequence, value.Budget, value.Actual, value.Commitment, value.Forecast)
            : new MonthlySeriesPointDto(period.Id, period.Name, period.Sequence, 0, 0, 0, 0)).ToList();

        var budget = totals?.Budget ?? 0m;
        var actual = totals?.Actual ?? 0m;
        var commitment = totals?.Commitment ?? 0m;
        var forecast = totals?.Forecast ?? 0m;
        return new DashboardSummaryDto(
            budget,
            actual,
            commitment,
            forecast,
            budget - actual - commitment,
            actual - budget,
            budget == 0 ? 0 : Math.Round(actual / budget * 100m, 2),
            monthly);
    }

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct))
            throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }
}
