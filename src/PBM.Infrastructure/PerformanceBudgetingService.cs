using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class PerformanceBudgetingService(
    PbmDbContext db,
    IUserContext user,
    IDashboardMetricPolicy metricPolicy) : IPerformanceBudgetingService
{
    public async Task<PerformanceBudgetScorecardDto> GetScorecardAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var version = await db.BudgetVersions.AsNoTracking()
            .Include(x => x.BudgetPlan).ThenInclude(x => x!.Company)
            .Include(x => x.BudgetPlan).ThenInclude(x => x!.FiscalYear)
            .Include(x => x.Scenario)
            .SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget version was not found.");

        var plan = version.BudgetPlan ?? throw new InvalidOperationException("Budget version has no budget plan.");
        var company = plan.Company ?? throw new InvalidOperationException("Budget plan has no company.");
        var fiscalYear = plan.FiscalYear ?? throw new InvalidOperationException("Budget plan has no fiscal year.");
        if (company.TenantId != user.TenantId)
            throw new UnauthorizedAccessException("Budget version is outside the current tenant.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(company.Id))
            throw new UnauthorizedAccessException("You do not have access to this company.");

        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == fiscalYear.Id)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);
        var today = DateTime.UtcNow.Date;
        var elapsedPeriods = periods.Where(x => x.StartDate.Date <= today || x.IsClosed).ToList();
        var elapsedPeriodIds = elapsedPeriods.Select(x => x.Id).ToHashSet();

        var measures = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == plan.BudgetModelId && x.ValueType == MeasureValueType.Amount)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);
        if (measures.Count == 0)
            throw new InvalidOperationException("The selected budget model has no amount-type measure for performance budgeting.");

        var availableCodes = measures.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedCode = metricPolicy.PreferredAmountMeasureCodes.FirstOrDefault(availableCodes.Contains);
        var selectedMeasure = selectedCode is null
            ? measures[0]
            : measures.First(x => x.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase));

        var baseCurrency = await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsBaseCurrency && x.IsActive)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? "IRR";

        var facts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == version.Id && x.MeasureId == selectedMeasure.Id)
            .ToListAsync(cancellationToken);

        var currencySignals = facts
            .GroupBy(x => NormalizeCurrency(x.CurrencyCode, baseCurrency), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key)
            .Select(group =>
            {
                var ytd = group.Where(x => elapsedPeriodIds.Contains(x.PeriodId)).ToList();
                return new PerformanceFundingCurrencySignal(
                    group.Key,
                    Sum(group, ValueKind.Budget),
                    Sum(group, ValueKind.Actual),
                    Sum(group, ValueKind.Commitment),
                    Sum(group, ValueKind.Forecast),
                    Sum(ytd, ValueKind.Budget),
                    Sum(ytd, ValueKind.Actual),
                    Sum(ytd, ValueKind.Commitment),
                    Sum(ytd, ValueKind.Forecast));
            })
            .ToList();

        var currencyDtos = currencySignals.Select(x => new PerformanceBudgetCurrencyDto(
            x.CurrencyCode,
            x.AnnualBudget,
            x.AnnualActual,
            x.AnnualCommitment,
            x.AnnualForecast,
            x.YtdBudget,
            x.YtdActual,
            x.YtdCommitment,
            x.YtdForecast,
            PerformanceFundingPolicy.Percent(x.YtdActual, x.YtdBudget),
            PerformanceFundingPolicy.Percent(x.YtdActual + x.YtdCommitment, x.YtdBudget),
            PerformanceFundingPolicy.Percent(x.AnnualForecast, x.AnnualBudget)))
            .ToList();

        var definitions = await db.Kpis.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var definitionIds = definitions.Select(x => x.Id).ToArray();
        var values = definitionIds.Length == 0
            ? []
            : await db.KpiValues.AsNoTracking()
                .Include(x => x.Period)
                .Where(x => definitionIds.Contains(x.KpiId)
                    && x.CompanyId == company.Id
                    && x.Period!.FiscalYearId == fiscalYear.Id
                    && (x.Period.StartDate <= today || x.Period.IsClosed))
                .ToListAsync(cancellationToken);

        var components = new List<PerformanceKpiComponentDto>();
        foreach (var definition in definitions)
        {
            var observations = values
                .Where(x => x.KpiId == definition.Id)
                .OrderBy(x => x.Period!.Sequence)
                .ToList();
            if (observations.Count == 0) continue;

            var scored = observations.Select(x => new
            {
                Value = x,
                Result = KpiScorePolicy.Evaluate(x.Target, x.Actual, definition.Minimum, definition.Maximum)
            }).ToList();
            var latest = scored[^1];
            components.Add(new PerformanceKpiComponentDto(
                definition.Id,
                definition.Code,
                definition.Name,
                latest.Result.Mode.ToString(),
                Math.Max(0m, definition.Weight),
                scored.Count,
                Round(scored.Average(x => x.Result.Score)),
                latest.Result.Score,
                latest.Result.IsOnTarget));
        }

        var totalDefinedWeight = definitions.Where(x => x.Weight > 0m).Sum(x => x.Weight);
        var observedWeight = components.Where(x => x.Weight > 0m).Sum(x => x.Weight);
        var dataCoverage = totalDefinedWeight > 0m
            ? PercentOrZero(observedWeight, totalDefinedWeight)
            : definitions.Count == 0
                ? 0m
                : PercentOrZero(components.Count, definitions.Count);

        decimal? weightedKpiScore = null;
        if (components.Count > 0)
        {
            weightedKpiScore = observedWeight > 0m
                ? Round(components.Where(x => x.Weight > 0m).Sum(x => x.AverageScore * x.Weight) / observedWeight)
                : Round(components.Average(x => x.AverageScore));
        }

        var decision = PerformanceFundingPolicy.Evaluate(dataCoverage, weightedKpiScore, currencySignals);
        var reasons = decision.Reasons.ToList();
        if (facts.Count == 0)
            reasons.Add($"No budget facts are recorded for measure {selectedMeasure.Code} in this version.");
        if (currencySignals.Count > 1)
            reasons.Add("Financial amounts are intentionally kept separate by currency; PBM does not add unlike currencies in this scorecard.");

        return new PerformanceBudgetScorecardDto(
            version.Id,
            company.Id,
            fiscalYear.Id,
            plan.BudgetModelId,
            version.Name,
            version.VersionNumber,
            version.Scenario?.Name ?? "-",
            fiscalYear.Name,
            selectedMeasure.Code,
            selectedMeasure.Name,
            elapsedPeriods.Count,
            periods.Count,
            dataCoverage,
            weightedKpiScore,
            decision.Recommendation,
            reasons,
            currencyDtos,
            components.OrderByDescending(x => x.Weight).ThenBy(x => x.Code).ToList());
    }

    private static decimal Sum(IEnumerable<BudgetFact> facts, ValueKind kind) =>
        facts.Where(x => x.ValueKind == kind).Sum(x => x.Value);

    private static string NormalizeCurrency(string? currencyCode, string baseCurrency) =>
        string.IsNullOrWhiteSpace(currencyCode) ? baseCurrency : currencyCode.Trim().ToUpperInvariant();

    private static decimal PercentOrZero(decimal numerator, decimal denominator) =>
        denominator == 0m ? 0m : Round(numerator / denominator * 100m);

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
