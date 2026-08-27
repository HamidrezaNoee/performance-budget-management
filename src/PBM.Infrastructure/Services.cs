using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class CompanyService(PbmDbContext db) : ICompanyService
{
    public async Task<IReadOnlyList<CompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default) =>
        await db.Companies.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new CompanyDto(x.Id, x.TenantId, x.Code, x.Name, x.Industry))
            .ToListAsync(cancellationToken);
}

public sealed class BudgetService(PbmDbContext db) : IBudgetService
{
    public async Task<IReadOnlyList<FiscalYearDto>> GetFiscalYearsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await db.FiscalYears.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.JalaliYear)
            .Select(x => new FiscalYearDto(x.Id, x.Code, x.Name, x.JalaliYear, x.StartDate, x.EndDate, x.IsClosed)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FiscalPeriodDto>> GetPeriodsAsync(Guid fiscalYearId, CancellationToken cancellationToken = default) =>
        await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYearId).OrderBy(x => x.Sequence)
            .Select(x => new FiscalPeriodDto(x.Id, x.Sequence, x.Code, x.Name, x.JalaliMonth, x.StartDate, x.EndDate, x.IsClosed)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BudgetModelDto>> GetModelsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var tenantId = await db.Companies.Where(x => x.Id == companyId).Select(x => x.TenantId).SingleAsync(cancellationToken);
        return await db.BudgetModels.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Name)
            .Select(x => new BudgetModelDto(x.Id, x.Code, x.Name, x.Description)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DimensionDto>> GetDimensionsAsync(Guid modelId, CancellationToken cancellationToken = default) =>
        await db.BudgetModelDimensions.AsNoTracking().Where(x => x.BudgetModelId == modelId).OrderBy(x => x.Sequence)
            .Select(x => new DimensionDto(x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence, x.IsRequired)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DimensionMemberDto>> GetDimensionMembersAsync(Guid dimensionId, Guid? companyId, CancellationToken cancellationToken = default) =>
        await db.DimensionMembers.AsNoTracking()
            .Where(x => x.DimensionId == dimensionId && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .OrderBy(x => x.Name)
            .Select(x => new DimensionMemberDto(x.Id, x.DimensionId, x.ParentId, x.CompanyId, x.Code, x.Name)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MeasureDto>> GetMeasuresAsync(Guid modelId, CancellationToken cancellationToken = default) =>
        await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == modelId).OrderBy(x => x.DisplayOrder)
            .Select(x => new MeasureDto(x.Id, x.Code, x.Name, x.Unit, x.ValueType, x.Aggregation, x.IsCalculated, x.FormulaExpression, x.DisplayOrder)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BudgetPlanDto>> GetPlansAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default) =>
        await db.BudgetPlans.AsNoTracking().Where(x => x.CompanyId == companyId && x.FiscalYearId == fiscalYearId)
            .OrderBy(x => x.Name)
            .Select(x => new BudgetPlanDto(x.Id, x.CompanyId, x.FiscalYearId, x.BudgetModelId, x.Name, x.Status,
                x.Versions.OrderBy(v => v.VersionNumber).Select(v => new BudgetVersionDto(v.Id, v.ScenarioId, v.VersionNumber, v.Name, v.Status, v.IsLocked)).ToList()))
            .ToListAsync(cancellationToken);

    public async Task<BudgetPlanDto> CreatePlanAsync(CreateBudgetPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Plan name is required.");

        var company = await db.Companies.SingleAsync(x => x.Id == request.CompanyId, cancellationToken);
        var fiscalYear = await db.FiscalYears.SingleAsync(x => x.Id == request.FiscalYearId && x.CompanyId == request.CompanyId, cancellationToken);
        var model = await db.BudgetModels.SingleAsync(x => x.Id == request.BudgetModelId && x.TenantId == company.TenantId, cancellationToken);
        var scenario = await db.BudgetScenarios.FirstAsync(x => x.TenantId == company.TenantId && x.Code == "BASE", cancellationToken);

        var plan = new BudgetPlan { CompanyId = company.Id, FiscalYearId = fiscalYear.Id, BudgetModelId = model.Id, Name = request.Name };
        var version = new BudgetVersion { BudgetPlanId = plan.Id, ScenarioId = scenario.Id, Name = "نسخه اولیه", VersionNumber = 1 };
        plan.Versions.Add(version);
        db.BudgetPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);

        return new BudgetPlanDto(plan.Id, plan.CompanyId, plan.FiscalYearId, plan.BudgetModelId, plan.Name, plan.Status,
            [new BudgetVersionDto(version.Id, version.ScenarioId, version.VersionNumber, version.Name, version.Status, version.IsLocked)]);
    }

    public async Task<Guid> UpsertFactAsync(UpsertBudgetFactRequest request, CancellationToken cancellationToken = default)
    {
        var version = await db.BudgetVersions.Include(x => x.BudgetPlan).SingleAsync(x => x.Id == request.VersionId, cancellationToken);
        if (version.IsLocked) throw new InvalidOperationException("Budget version is locked.");

        var periodIsValid = await db.FiscalPeriods.AnyAsync(x => x.Id == request.PeriodId && x.FiscalYearId == version.BudgetPlan!.FiscalYearId, cancellationToken);
        if (!periodIsValid) throw new ArgumentException("Period does not belong to the budget plan fiscal year.");

        var measureIsValid = await db.Measures.AnyAsync(x => x.Id == request.MeasureId && x.BudgetModelId == version.BudgetPlan!.BudgetModelId, cancellationToken);
        if (!measureIsValid) throw new ArgumentException("Measure does not belong to the budget model.");

        var modelDimensions = await db.BudgetModelDimensions.Where(x => x.BudgetModelId == version.BudgetPlan!.BudgetModelId).ToListAsync(cancellationToken);
        var allowedDimensions = modelDimensions.Select(x => x.DimensionId).ToHashSet();
        var supplied = request.Dimensions.Select(x => x.DimensionId).Distinct().ToHashSet();
        if (modelDimensions.Where(x => x.IsRequired).Any(x => !supplied.Contains(x.DimensionId))) throw new ArgumentException("One or more required dimensions are missing.");
        if (supplied.Any(x => !allowedDimensions.Contains(x))) throw new ArgumentException("A supplied dimension does not belong to the budget model.");
        if (supplied.Count != request.Dimensions.Count) throw new ArgumentException("A dimension can only be supplied once.");

        foreach (var selection in request.Dimensions)
        {
            var memberIsValid = await db.DimensionMembers.AnyAsync(x => x.Id == selection.MemberId && x.DimensionId == selection.DimensionId, cancellationToken);
            if (!memberIsValid) throw new ArgumentException("Invalid dimension member selection.");
        }

        var hash = BudgetCoordinateKey.Create(request.Dimensions);
        var coordinatesJson = JsonSerializer.Serialize(request.Dimensions.OrderBy(x => x.DimensionId));
        var fact = await db.BudgetFacts.Include(x => x.Dimensions).SingleOrDefaultAsync(x => x.VersionId == request.VersionId && x.PeriodId == request.PeriodId && x.MeasureId == request.MeasureId && x.ValueKind == request.ValueKind && x.CoordinateHash == hash, cancellationToken);

        if (fact is null)
        {
            fact = new BudgetFact { VersionId = request.VersionId, PeriodId = request.PeriodId, MeasureId = request.MeasureId, ValueKind = request.ValueKind, CoordinateHash = hash, CoordinatesJson = coordinatesJson };
            db.BudgetFacts.Add(fact);
        }
        else
        {
            db.BudgetFactDimensions.RemoveRange(fact.Dimensions);
            fact.Dimensions.Clear();
        }

        fact.Value = request.Value;
        fact.CurrencyCode = request.CurrencyCode;
        fact.Source = request.Source;
        fact.Note = request.Note;
        fact.UpdatedAtUtc = DateTime.UtcNow;
        foreach (var selection in request.Dimensions)
            fact.Dimensions.Add(new BudgetFactDimension { BudgetFactId = fact.Id, DimensionId = selection.DimensionId, MemberId = selection.MemberId });

        await db.SaveChangesAsync(cancellationToken);
        return fact.Id;
    }

    public async Task<BudgetGridDto> GetGridAsync(BudgetGridQuery query, CancellationToken cancellationToken = default)
    {
        var version = await db.BudgetVersions.AsNoTracking().Include(x => x.BudgetPlan).SingleAsync(x => x.Id == query.VersionId, cancellationToken);
        var plan = version.BudgetPlan!;
        var rowDimension = await db.BudgetModelDimensions.AsNoTracking().Where(x => x.BudgetModelId == plan.BudgetModelId && x.DimensionId == query.RowDimensionId)
            .Select(x => new DimensionDto(x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence, x.IsRequired)).SingleAsync(cancellationToken);
        var measure = await db.Measures.AsNoTracking().Where(x => x.Id == query.MeasureId && x.BudgetModelId == plan.BudgetModelId)
            .Select(x => new MeasureDto(x.Id, x.Code, x.Name, x.Unit, x.ValueType, x.Aggregation, x.IsCalculated, x.FormulaExpression, x.DisplayOrder)).SingleAsync(cancellationToken);
        var periods = await GetPeriodsAsync(plan.FiscalYearId, cancellationToken);
        var members = await GetDimensionMembersAsync(query.RowDimensionId, plan.CompanyId, cancellationToken);

        var factQuery = db.BudgetFacts.AsNoTracking().Include(x => x.Dimensions)
            .Where(x => x.VersionId == query.VersionId && x.MeasureId == query.MeasureId && x.ValueKind == query.ValueKind);
        foreach (var filter in query.Filters)
            factQuery = factQuery.Where(x => x.Dimensions.Any(d => d.DimensionId == filter.DimensionId && d.MemberId == filter.MemberId));

        var facts = await factQuery.ToListAsync(cancellationToken);
        var values = facts.Select(x => new
        {
            Fact = x,
            RowMemberId = x.Dimensions.Where(d => d.DimensionId == query.RowDimensionId).Select(d => (Guid?)d.MemberId).SingleOrDefault()
        }).Where(x => x.RowMemberId.HasValue).ToDictionary(x => (x.RowMemberId!.Value, x.Fact.PeriodId));

        var rows = members.Select(member => new BudgetGridRowDto(member.Id, member.Code, member.Name,
            periods.Select(period => values.TryGetValue((member.Id, period.Id), out var item)
                ? new BudgetGridCellDto(period.Id, item.Fact.Id, item.Fact.Value)
                : new BudgetGridCellDto(period.Id, null, 0)).ToList())).ToList();

        return new BudgetGridDto(periods, measure, rowDimension, rows);
    }
}

public sealed class DashboardService(PbmDbContext db) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        var query = db.BudgetFacts.AsNoTracking().Where(x => x.Version!.BudgetPlan!.CompanyId == companyId && x.Version.BudgetPlan.FiscalYearId == fiscalYearId);
        var totals = await query.GroupBy(_ => 1).Select(g => new
        {
            Budget = g.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value), Actual = g.Where(x => x.ValueKind == ValueKind.Actual).Sum(x => x.Value),
            Commitment = g.Where(x => x.ValueKind == ValueKind.Commitment).Sum(x => x.Value), Forecast = g.Where(x => x.ValueKind == ValueKind.Forecast).Sum(x => x.Value)
        }).SingleOrDefaultAsync(cancellationToken);

        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYearId).OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        var grouped = await query.GroupBy(x => x.PeriodId).Select(g => new
        {
            PeriodId = g.Key, Budget = g.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value), Actual = g.Where(x => x.ValueKind == ValueKind.Actual).Sum(x => x.Value),
            Commitment = g.Where(x => x.ValueKind == ValueKind.Commitment).Sum(x => x.Value), Forecast = g.Where(x => x.ValueKind == ValueKind.Forecast).Sum(x => x.Value)
        }).ToDictionaryAsync(x => x.PeriodId, cancellationToken);

        var monthly = periods.Select(p => grouped.TryGetValue(p.Id, out var x)
            ? new MonthlySeriesPointDto(p.Id, p.Name, p.Sequence, x.Budget, x.Actual, x.Commitment, x.Forecast)
            : new MonthlySeriesPointDto(p.Id, p.Name, p.Sequence, 0, 0, 0, 0)).ToList();

        var budget = totals?.Budget ?? 0; var actual = totals?.Actual ?? 0; var commitment = totals?.Commitment ?? 0; var forecast = totals?.Forecast ?? 0;
        return new DashboardSummaryDto(budget, actual, commitment, forecast, budget - actual - commitment, actual - budget,
            budget == 0 ? 0 : Math.Round(actual / budget * 100, 2), monthly);
    }
}
