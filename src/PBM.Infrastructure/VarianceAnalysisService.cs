using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class VarianceAnalysisService(PbmDbContext db, IUserContext user) : IVarianceAnalysisService
{
    public async Task<VarianceAnalysisDto> AnalyzeAsync(VarianceAnalysisQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(query.CompanyId, cancellationToken);
        var take = Math.Clamp(query.Take, 1, 200);

        var plan = await db.BudgetPlans.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId
                && x.FiscalYearId == query.FiscalYearId
                && x.BudgetModelId == query.BudgetModelId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget plan was not found for the selected company, fiscal year and model.");

        var version = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlanId == plan.Id && x.Status != BudgetStatus.Rejected)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("No usable budget version was found.");

        var measure = await db.Measures.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == query.MeasureId && x.BudgetModelId == query.BudgetModelId, cancellationToken)
            ?? throw new ArgumentException("Measure does not belong to the selected budget model.");

        var rowDimension = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == query.BudgetModelId && x.DimensionId == query.RowDimensionId)
            .Select(x => new DimensionDto(x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence, x.IsRequired))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Row dimension does not belong to the selected budget model.");

        var allowedDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == query.BudgetModelId)
            .Select(x => x.DimensionId)
            .ToListAsync(cancellationToken);
        if (query.Filters.Select(x => x.DimensionId).Distinct().Count() != query.Filters.Count)
            throw new ArgumentException("A dimension filter can only be supplied once.");
        if (query.Filters.Any(x => x.DimensionId == query.RowDimensionId || !allowedDimensions.Contains(x.DimensionId)))
            throw new ArgumentException("One or more variance filters are invalid for the selected model.");

        var factQuery = db.BudgetFacts.AsNoTracking().Include(x => x.Period).Include(x => x.Dimensions)
            .Where(x => x.VersionId == version.Id && x.MeasureId == query.MeasureId);
        foreach (var filter in query.Filters)
            factQuery = factQuery.Where(x => x.Dimensions.Any(d => d.DimensionId == filter.DimensionId && d.MemberId == filter.MemberId));

        var facts = await factQuery.ToListAsync(cancellationToken);
        var rowMemberIds = facts.SelectMany(x => x.Dimensions)
            .Where(x => x.DimensionId == query.RowDimensionId)
            .Select(x => x.MemberId)
            .Distinct()
            .ToArray();
        var memberMap = await db.DimensionMembers.AsNoTracking()
            .Where(x => rowMemberIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var grouped = facts.Select(x => new
            {
                Fact = x,
                MemberId = x.Dimensions.Where(d => d.DimensionId == query.RowDimensionId).Select(d => (Guid?)d.MemberId).SingleOrDefault()
            })
            .Where(x => x.MemberId.HasValue)
            .GroupBy(x => x.MemberId!.Value)
            .Select(group =>
            {
                var member = memberMap[group.Key];
                var budget = Aggregate(group.Where(x => x.Fact.ValueKind == ValueKind.Budget).Select(x => x.Fact), measure.Aggregation);
                var actual = Aggregate(group.Where(x => x.Fact.ValueKind == ValueKind.Actual).Select(x => x.Fact), measure.Aggregation);
                var commitment = Aggregate(group.Where(x => x.Fact.ValueKind == ValueKind.Commitment).Select(x => x.Fact), measure.Aggregation);
                var forecast = Aggregate(group.Where(x => x.Fact.ValueKind == ValueKind.Forecast).Select(x => x.Fact), measure.Aggregation);
                var variance = actual - budget;
                return new VarianceAnalysisItemDto(
                    member.Id,
                    member.Code,
                    member.Name,
                    budget,
                    actual,
                    commitment,
                    forecast,
                    variance,
                    budget == 0m ? null : Math.Round(variance / Math.Abs(budget) * 100m, 2),
                    budget == 0m ? null : Math.Round(actual / budget * 100m, 2));
            })
            .OrderByDescending(x => Math.Abs(x.Variance))
            .Take(take)
            .ToList();

        var all = facts.GroupBy(x => x.ValueKind).ToDictionary(x => x.Key, x => Aggregate(x, measure.Aggregation));
        return new VarianceAnalysisDto(
            version.Id,
            version.VersionNumber,
            new MeasureDto(measure.Id, measure.Code, measure.Name, measure.Unit, measure.ValueType, measure.Aggregation, measure.IsCalculated, measure.FormulaExpression, measure.DisplayOrder),
            rowDimension,
            all.GetValueOrDefault(ValueKind.Budget),
            all.GetValueOrDefault(ValueKind.Actual),
            all.GetValueOrDefault(ValueKind.Commitment),
            all.GetValueOrDefault(ValueKind.Forecast),
            grouped);
    }

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        var exists = await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct);
        if (!exists) throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private static decimal Aggregate(IEnumerable<BudgetFact> facts, MeasureAggregation aggregation)
    {
        var materialized = facts.ToList();
        if (materialized.Count == 0) return 0m;
        var values = materialized.Select(x => x.Value).ToList();
        return aggregation switch
        {
            MeasureAggregation.Average => values.Average(),
            MeasureAggregation.Min => values.Min(),
            MeasureAggregation.Max => values.Max(),
            MeasureAggregation.LastNonEmpty => materialized.OrderByDescending(x => x.Period?.Sequence ?? 0).First().Value,
            MeasureAggregation.None => materialized.OrderByDescending(x => x.UpdatedAtUtc).First().Value,
            _ => values.Sum()
        };
    }
}
