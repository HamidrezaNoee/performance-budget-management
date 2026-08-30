using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ExecutiveDashboardService(PbmDbContext db, IUserContext user, IDashboardMetricPolicy metricPolicy)
    : IDashboardService, IDashboardAnalyticsService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        var periods = await ValidateAndGetPeriodsAsync(companyId, fiscalYearId, cancellationToken);
        var metric = await ResolveMetricAsync(companyId, fiscalYearId, null, cancellationToken);
        if (metric is null) return EmptySummary(periods);

        return await BuildSummaryAsync(companyId, fiscalYearId, periods, metric, cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardMetricOptionDto>> GetMetricOptionsAsync(
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        await ValidateAndGetPeriodsAsync(companyId, fiscalYearId, cancellationToken);
        var candidates = await GetMetricCandidatesAsync(companyId, fiscalYearId, cancellationToken);
        var baseCurrency = await GetBaseCurrencyAsync(cancellationToken);

        return candidates
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).First();
                return new DashboardMetricOptionDto(first.Code, first.Name, first.Unit, baseCurrency, first.DisplayOrder);
            })
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<DashboardMeasureSummaryDto> GetSummaryForMeasureAsync(
        Guid companyId,
        Guid fiscalYearId,
        string measureCode,
        CancellationToken cancellationToken = default)
    {
        var periods = await ValidateAndGetPeriodsAsync(companyId, fiscalYearId, cancellationToken);
        var metric = await ResolveMetricAsync(companyId, fiscalYearId, measureCode, cancellationToken)
            ?? throw new KeyNotFoundException($"Amount measure '{measureCode}' is not available for the selected company and fiscal year.");
        var summary = await BuildSummaryAsync(companyId, fiscalYearId, periods, metric, cancellationToken);
        var currency = await GetBaseCurrencyAsync(cancellationToken);
        return new DashboardMeasureSummaryDto(metric.Code, metric.Name, metric.Unit, currency, summary);
    }

    public async Task<IReadOnlyList<DashboardDimensionOptionDto>> GetDrilldownDimensionsAsync(
        Guid companyId,
        Guid fiscalYearId,
        string measureCode,
        CancellationToken cancellationToken = default)
    {
        await ValidateAndGetPeriodsAsync(companyId, fiscalYearId, cancellationToken);
        var metric = await ResolveMetricAsync(companyId, fiscalYearId, measureCode, cancellationToken)
            ?? throw new KeyNotFoundException($"Amount measure '{measureCode}' is not available for the selected company and fiscal year.");

        var rows = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => metric.ModelIds.Contains(x.BudgetModelId) && x.Dimension!.IsActive)
            .Select(x => new { x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.DimensionId)
            .Select(group =>
            {
                var first = group.OrderBy(x => x.Sequence).First();
                return new DashboardDimensionOptionDto(first.DimensionId, first.Code, first.Name, group.Min(x => x.Sequence));
            })
            .OrderBy(x => x.Sequence)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<DashboardDrilldownDto> GetDrilldownAsync(
        Guid companyId,
        Guid fiscalYearId,
        string measureCode,
        Guid dimensionId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        await ValidateAndGetPeriodsAsync(companyId, fiscalYearId, cancellationToken);
        take = Math.Clamp(take, 1, 500);

        var metric = await ResolveMetricAsync(companyId, fiscalYearId, measureCode, cancellationToken)
            ?? throw new KeyNotFoundException($"Amount measure '{measureCode}' is not available for the selected company and fiscal year.");

        var dimension = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => metric.ModelIds.Contains(x.BudgetModelId) && x.DimensionId == dimensionId && x.Dimension!.IsActive)
            .Select(x => new { x.DimensionId, x.Dimension!.Code, x.Dimension.Name })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Selected dimension is not available for the requested dashboard measure.");

        var latestVersionIds = await GetLatestVersionIdsAsync(companyId, fiscalYearId, metric.ModelIds, cancellationToken);
        var currency = await GetBaseCurrencyAsync(cancellationToken);
        if (latestVersionIds.Length == 0)
            return new DashboardDrilldownDto(dimension.DimensionId, dimension.Code, dimension.Name, metric.Code, metric.Name, metric.Unit, currency, 0, []);

        var factQuery = db.BudgetFactDimensions.AsNoTracking().Where(x =>
            x.DimensionId == dimensionId
            && latestVersionIds.Contains(x.BudgetFact!.VersionId)
            && x.BudgetFact.Measure!.ValueType == MeasureValueType.Amount
            && x.BudgetFact.Measure.Code == metric.Code
            && x.BudgetFact.CurrencyCode == currency);

        var totalMemberCount = await factQuery.Select(x => x.MemberId).Distinct().CountAsync(cancellationToken);
        var grouped = await factQuery
            .GroupBy(x => x.MemberId)
            .Select(group => new
            {
                MemberId = group.Key,
                Budget = group.Where(x => x.BudgetFact!.ValueKind == ValueKind.Budget).Sum(x => x.BudgetFact!.Value),
                Actual = group.Where(x => x.BudgetFact!.ValueKind == ValueKind.Actual).Sum(x => x.BudgetFact!.Value),
                Commitment = group.Where(x => x.BudgetFact!.ValueKind == ValueKind.Commitment).Sum(x => x.BudgetFact!.Value),
                Forecast = group.Where(x => x.BudgetFact!.ValueKind == ValueKind.Forecast).Sum(x => x.BudgetFact!.Value)
            })
            .OrderByDescending(x => x.Actual)
            .ThenByDescending(x => x.Budget)
            .Take(take)
            .ToListAsync(cancellationToken);

        var memberIds = grouped.Select(x => x.MemberId).ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => memberIds.Contains(x.Id) && x.DimensionId == dimensionId && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var rows = grouped
            .Where(x => members.ContainsKey(x.MemberId))
            .Select(x =>
            {
                var member = members[x.MemberId];
                return new DashboardDrilldownRowDto(
                    x.MemberId,
                    member.Code,
                    member.Name,
                    x.Budget,
                    x.Actual,
                    x.Commitment,
                    x.Forecast,
                    x.Budget - x.Actual - x.Commitment,
                    x.Actual - x.Budget,
                    x.Budget == 0 ? 0 : Math.Round(x.Actual / x.Budget * 100m, 2));
            })
            .ToList();

        return new DashboardDrilldownDto(
            dimension.DimensionId,
            dimension.Code,
            dimension.Name,
            metric.Code,
            metric.Name,
            metric.Unit,
            currency,
            totalMemberCount,
            rows);
    }

    private async Task<DashboardSummaryDto> BuildSummaryAsync(
        Guid companyId,
        Guid fiscalYearId,
        IReadOnlyList<FiscalPeriod> periods,
        ResolvedMetric metric,
        CancellationToken cancellationToken)
    {
        var latestVersionIds = await GetLatestVersionIdsAsync(companyId, fiscalYearId, metric.ModelIds, cancellationToken);
        if (latestVersionIds.Length == 0) return EmptySummary(periods);

        var baseCurrency = await GetBaseCurrencyAsync(cancellationToken);
        var query = db.BudgetFacts.AsNoTracking().Where(x =>
            latestVersionIds.Contains(x.VersionId)
            && x.Measure!.ValueType == MeasureValueType.Amount
            && x.Measure.Code == metric.Code
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

    private async Task<ResolvedMetric?> ResolveMetricAsync(
        Guid companyId,
        Guid fiscalYearId,
        string? requestedCode,
        CancellationToken cancellationToken)
    {
        var candidates = await GetMetricCandidatesAsync(companyId, fiscalYearId, cancellationToken);
        if (candidates.Count == 0) return null;

        string selectedCode;
        if (!string.IsNullOrWhiteSpace(requestedCode))
        {
            var normalized = requestedCode.Trim().ToUpperInvariant();
            selectedCode = candidates.FirstOrDefault(x => x.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase))?.Code
                ?? throw new KeyNotFoundException($"Amount measure '{requestedCode}' is not available for the selected company and fiscal year.");
        }
        else
        {
            var availableCodes = candidates.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            selectedCode = metricPolicy.PreferredAmountMeasureCodes.FirstOrDefault(availableCodes.Contains)
                ?? candidates.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => x.Code).First();
        }

        var selected = candidates.Where(x => x.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase)).ToList();
        var first = selected.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).First();
        return new ResolvedMetric(first.Code, first.Name, first.Unit, selected.Select(x => x.ModelId).Distinct().ToArray());
    }

    private async Task<List<MetricCandidate>> GetMetricCandidatesAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken)
    {
        var modelIds = await db.BudgetPlans.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalYearId == fiscalYearId)
            .Select(x => x.BudgetModelId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (modelIds.Length == 0) return [];

        var rows = await db.Measures.AsNoTracking()
            .Where(x => modelIds.Contains(x.BudgetModelId) && x.ValueType == MeasureValueType.Amount)
            .Select(x => new { x.BudgetModelId, x.Code, x.Name, x.Unit, x.DisplayOrder })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new MetricCandidate(x.BudgetModelId, x.Code, x.Name, x.Unit, x.DisplayOrder)).ToList();
    }

    private async Task<Guid[]> GetLatestVersionIdsAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid[] modelIds,
        CancellationToken cancellationToken)
    {
        var versionRefs = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.CompanyId == companyId
                && x.BudgetPlan.FiscalYearId == fiscalYearId
                && modelIds.Contains(x.BudgetPlan.BudgetModelId))
            .Select(x => new { x.Id, x.BudgetPlanId, x.VersionNumber, x.Status })
            .ToListAsync(cancellationToken);

        return versionRefs
            .Where(x => x.Status != BudgetStatus.Rejected)
            .GroupBy(x => x.BudgetPlanId)
            .Select(group => group.OrderByDescending(x => x.VersionNumber).First().Id)
            .ToArray();
    }

    private async Task<IReadOnlyList<FiscalPeriod>> ValidateAndGetPeriodsAsync(
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken)
    {
        await EnsureCompanyAsync(companyId, cancellationToken);
        if (!await db.FiscalYears.AnyAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");

        return await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == fiscalYearId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);
    }

    private async Task<string> GetBaseCurrencyAsync(CancellationToken cancellationToken) =>
        await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsBaseCurrency && x.IsActive)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? "IRR";

    private static DashboardSummaryDto EmptySummary(IReadOnlyList<FiscalPeriod> periods) =>
        new(0, 0, 0, 0, 0, 0, 0,
            periods.Select(x => new MonthlySeriesPointDto(x.Id, x.Name, x.Sequence, 0, 0, 0, 0)).ToList());

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct))
            throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private sealed record MetricCandidate(Guid ModelId, string Code, string Name, string? Unit, int DisplayOrder);
    private sealed record ResolvedMetric(string Code, string Name, string? Unit, Guid[] ModelIds);
}
