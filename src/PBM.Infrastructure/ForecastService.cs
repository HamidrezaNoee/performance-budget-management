using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ForecastService(PbmDbContext db, IUserContext user) : IForecastService
{
    public async Task<ForecastResultDto> GenerateAsync(Guid companyId, Guid fiscalYearId, Guid measureId, ForecastMethod method, CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, cancellationToken);
        var measure = await db.Measures.AsNoTracking().Where(x => x.Id == measureId)
            .Select(x => new { x.Id, x.Name, x.BudgetModelId }).SingleAsync(cancellationToken);
        var modelBelongsToTenant = await db.BudgetModels.AnyAsync(x => x.Id == measure.BudgetModelId && x.TenantId == user.TenantId, cancellationToken);
        if (!modelBelongsToTenant) throw new UnauthorizedAccessException("Measure is outside the current tenant.");

        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYearId && x.FiscalYear!.CompanyId == companyId).OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        if (periods.Count == 0) throw new KeyNotFoundException("No fiscal periods were found.");

        var actuals = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.Version!.BudgetPlan!.CompanyId == companyId && x.Version.BudgetPlan.FiscalYearId == fiscalYearId && x.MeasureId == measureId && x.ValueKind == ValueKind.Actual)
            .GroupBy(x => x.PeriodId).Select(g => new { PeriodId = g.Key, Value = g.Sum(x => x.Value) }).ToDictionaryAsync(x => x.PeriodId, x => x.Value, cancellationToken);

        var observed = periods.Where(p => actuals.ContainsKey(p.Id)).Select(p => (X: (decimal)p.Sequence, Y: actuals[p.Id])).ToList();
        if (observed.Count == 0) throw new InvalidOperationException("At least one period with Actual data is required for forecasting.");

        return method switch
        {
            ForecastMethod.MovingAverage3 => BuildMovingAverage(companyId, fiscalYearId, measureId, measure.Name, periods, actuals),
            _ => BuildLinear(companyId, fiscalYearId, measureId, measure.Name, periods, actuals, observed)
        };
    }

    private static ForecastResultDto BuildLinear(Guid companyId, Guid fiscalYearId, Guid measureId, string measureName, IReadOnlyList<FiscalPeriod> periods, IReadOnlyDictionary<Guid, decimal> actuals, IReadOnlyList<(decimal X, decimal Y)> observed)
    {
        decimal slope = 0, intercept = observed.Average(x => x.Y), rSquared = 0;
        if (observed.Count >= 2)
        {
            var meanX = observed.Average(x => x.X); var meanY = observed.Average(x => x.Y);
            var denominator = observed.Sum(x => (x.X - meanX) * (x.X - meanX));
            if (denominator != 0) slope = observed.Sum(x => (x.X - meanX) * (x.Y - meanY)) / denominator;
            intercept = meanY - slope * meanX;
            var ssTotal = observed.Sum(x => (x.Y - meanY) * (x.Y - meanY));
            var ssResidual = observed.Sum(x => { var predicted = intercept + slope * x.X; return (x.Y - predicted) * (x.Y - predicted); });
            rSquared = ssTotal == 0 ? 1 : Math.Max(0, 1 - ssResidual / ssTotal);
        }
        var lastObservedSequence = observed.Max(x => (int)x.X);
        var points = periods.Select(p => new ForecastPointDto(p.Id, p.Name, p.Sequence, actuals.TryGetValue(p.Id, out var actual) ? actual : null, Math.Max(0, intercept + slope * p.Sequence), p.Sequence > lastObservedSequence)).ToList();
        return new ForecastResultDto(companyId, fiscalYearId, measureId, measureName, ForecastMethod.LinearTrend, slope, intercept, Math.Round(rSquared, 6), points);
    }

    private static ForecastResultDto BuildMovingAverage(Guid companyId, Guid fiscalYearId, Guid measureId, string measureName, IReadOnlyList<FiscalPeriod> periods, IReadOnlyDictionary<Guid, decimal> actuals)
    {
        var history = new List<decimal>(); var points = new List<ForecastPointDto>(); var lastActualSequence = periods.Where(p => actuals.ContainsKey(p.Id)).Max(p => p.Sequence);
        foreach (var period in periods)
        {
            var hasActual = actuals.TryGetValue(period.Id, out var actual);
            decimal predicted;
            if (history.Count == 0) predicted = hasActual ? actual : 0;
            else predicted = history.TakeLast(Math.Min(3, history.Count)).Average();
            points.Add(new ForecastPointDto(period.Id, period.Name, period.Sequence, hasActual ? actual : null, Math.Max(0, predicted), period.Sequence > lastActualSequence));
            history.Add(hasActual ? actual : predicted);
        }
        return new ForecastResultDto(companyId, fiscalYearId, measureId, measureName, ForecastMethod.MovingAverage3, null, null, null, points);
    }

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId, ct)) throw new UnauthorizedAccessException("Company is outside the current tenant.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
    }
}
