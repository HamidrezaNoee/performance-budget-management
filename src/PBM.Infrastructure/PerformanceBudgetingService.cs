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
        var elapsedPeriodIdArray = elapsedPeriodIds.ToArray();

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
        List<KpiValue> values;
        if (definitionIds.Length == 0 || elapsedPeriodIdArray.Length == 0)
        {
            values = [];
        }
        else
        {
            values = await db.KpiValues.AsNoTracking()
                .Include(x => x.Period)
                .Where(x => definitionIds.Contains(x.KpiId)
                    && x.CompanyId == company.Id
                    && elapsedPeriodIdArray.Contains(x.PeriodId))
                .ToListAsync(cancellationToken);
        }

        var components = new List<PerformanceKpiComponentDto>();
        foreach (var definition in definitions)
        {
            var observations = values
                .Where(x => x.KpiId == definition.Id)
                .OrderBy(x => x.Period?.Sequence ?? int.MaxValue)
                .ToList();
            if (observations.Count == 0) continue;

            var scored = observations.Select(x => new
            {
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
        var kpiCoverage = totalDefinedWeight > 0m
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

        var objectives = await db.StrategicObjectives.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsActive)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var objectiveIds = objectives.Select(x => x.Id).ToArray();
        List<KpiObjectiveLink> links;
        if (objectiveIds.Length == 0 || definitionIds.Length == 0)
        {
            links = [];
        }
        else
        {
            links = await db.KpiObjectiveLinks.AsNoTracking()
                .Where(x => objectiveIds.Contains(x.ObjectiveId) && definitionIds.Contains(x.KpiId))
                .ToListAsync(cancellationToken);
        }

        var componentByKpiId = components.ToDictionary(x => x.KpiId);
        var objectiveComponents = new List<PerformanceObjectiveComponentDto>();
        foreach (var objective in objectives)
        {
            var objectiveLinks = links.Where(x => x.ObjectiveId == objective.Id && x.Weight > 0m).ToList();
            var totalLinkWeight = objectiveLinks.Sum(x => x.Weight);
            var observedLinks = objectiveLinks.Where(x => componentByKpiId.ContainsKey(x.KpiId)).ToList();
            var observedLinkWeight = observedLinks.Sum(x => x.Weight);
            decimal? objectiveScore = observedLinkWeight > 0m
                ? Round(observedLinks.Sum(x => componentByKpiId[x.KpiId].AverageScore * x.Weight) / observedLinkWeight)
                : null;
            var objectiveCoverage = totalLinkWeight > 0m
                ? PercentOrZero(observedLinkWeight, totalLinkWeight)
                : 0m;

            objectiveComponents.Add(new PerformanceObjectiveComponentDto(
                objective.Id,
                objective.Code,
                objective.Name,
                Math.Max(0m, objective.Weight),
                objectiveLinks.Select(x => x.KpiId).Distinct().Count(),
                observedLinks.Select(x => x.KpiId).Distinct().Count(),
                objectiveCoverage,
                objectiveScore));
        }

        var totalStrategicWeight = objectiveComponents.Where(x => x.StrategicWeight > 0m).Sum(x => x.StrategicWeight);
        var observedStrategicWeight = objectiveComponents.Where(x => x.StrategicWeight > 0m && x.Score.HasValue).Sum(x => x.StrategicWeight);
        var strategyCoverage = totalStrategicWeight > 0m
            ? PercentOrZero(observedStrategicWeight, totalStrategicWeight)
            : objectiveComponents.Count == 0
                ? 0m
                : PercentOrZero(objectiveComponents.Count(x => x.Score.HasValue), objectiveComponents.Count);

        decimal? strategyWeightedScore = null;
        var scoredObjectives = objectiveComponents.Where(x => x.Score.HasValue).ToList();
        if (scoredObjectives.Count > 0)
        {
            strategyWeightedScore = observedStrategicWeight > 0m
                ? Round(scoredObjectives.Where(x => x.StrategicWeight > 0m).Sum(x => x.Score!.Value * x.StrategicWeight) / observedStrategicWeight)
                : Round(scoredObjectives.Average(x => x.Score!.Value));
        }

        var strategyConfigured = objectives.Count > 0;
        var recommendationScore = strategyConfigured ? strategyWeightedScore : weightedKpiScore;
        var effectiveCoverage = strategyConfigured ? Math.Min(kpiCoverage, strategyCoverage) : kpiCoverage;
        var decision = PerformanceFundingPolicy.Evaluate(effectiveCoverage, recommendationScore, currencySignals);
        var reasons = decision.Reasons.ToList();
        if (strategyConfigured)
        {
            reasons.Add(strategyWeightedScore.HasValue
                ? $"Funding recommendation uses the strategic-objective weighted score ({strategyWeightedScore.Value:0.##}%)."
                : "Strategic objectives are configured, but no objective has enough linked KPI observations to produce a strategic score.");
        }
        else
        {
            reasons.Add("No strategic objectives are configured; the funding recommendation currently uses the weighted KPI score directly.");
        }
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
            effectiveCoverage,
            weightedKpiScore,
            strategyCoverage,
            strategyWeightedScore,
            recommendationScore,
            decision.Recommendation,
            reasons,
            currencyDtos,
            components.OrderByDescending(x => x.Weight).ThenBy(x => x.Code).ToList(),
            objectiveComponents.OrderByDescending(x => x.StrategicWeight).ThenBy(x => x.Code).ToList());
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
