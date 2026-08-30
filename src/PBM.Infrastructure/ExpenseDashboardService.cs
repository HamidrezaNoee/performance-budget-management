using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ExpenseDashboardService(
    PbmDbContext db,
    IUserContext user,
    CommercialPlanningProvisioner provisioner) : IExpenseDashboardService
{
    private const string ModelCode = "EXPENSE";
    private const string MeasureCode = "EXPENSE_AMOUNT";
    private const string ClassCode = "EXPENSECLASS";
    private const string ItemCode = "EXPENSEITEM";

    private static readonly HashSet<string> IncomeClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "OTHER_OPERATING_INCOME", "OTHER_NON_OPERATING_INCOME"
    };

    public async Task<ExpenseDashboardDto?> GetAsync(Guid companyId, Guid fiscalYearId, Guid? dimensionId = null, int take = 50, CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, cancellationToken);
        var tenantId = await db.Companies.AsNoTracking().Where(x => x.Id == companyId).Select(x => x.TenantId).SingleAsync(cancellationToken);
        await provisioner.EnsureExpenseAsync(tenantId, cancellationToken);
        if (!await db.FiscalYears.AsNoTracking().AnyAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");

        var version = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.CompanyId == companyId && x.BudgetPlan.FiscalYearId == fiscalYearId
                && x.BudgetPlan.BudgetModel!.Code == ModelCode && x.Status != BudgetStatus.Rejected)
            .OrderByDescending(x => x.VersionNumber).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.Id, x.VersionNumber, x.Name, ModelId = x.BudgetPlan!.BudgetModelId })
            .FirstOrDefaultAsync(cancellationToken);
        if (version is null) return null;

        var measureId = await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == version.ModelId && x.Code == MeasureCode).Select(x => x.Id).SingleAsync(cancellationToken);
        var classDimension = await db.Dimensions.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.Code == ClassCode && x.IsActive, cancellationToken);
        var classMembers = await db.DimensionMembers.AsNoTracking()
            .Where(x => x.DimensionId == classDimension.Id && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new ClassMember(x.Id, x.Code, x.Name)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var currency = await GetBaseCurrencyAsync(tenantId, cancellationToken);

        var facts = await db.BudgetFacts.AsNoTracking().Include(x => x.Dimensions)
            .Where(x => x.VersionId == version.Id && x.MeasureId == measureId
                && (x.ValueKind == ValueKind.Budget || x.ValueKind == ValueKind.Forecast)
                && x.CurrencyCode == currency)
            .ToListAsync(cancellationToken);

        ClassMember? ClassOf(BudgetFact fact)
        {
            var id = fact.Dimensions.Where(x => x.DimensionId == classDimension.Id).Select(x => (Guid?)x.MemberId).SingleOrDefault();
            return id.HasValue && classMembers.TryGetValue(id.Value, out var member) ? member : null;
        }
        bool IsIncome(BudgetFact fact) => ClassOf(fact) is { } c && IncomeClasses.Contains(c.Code);
        decimal Sum(ValueKind kind, bool income, IEnumerable<BudgetFact>? source = null) => (source ?? facts)
            .Where(x => x.ValueKind == kind && IsIncome(x) == income).Sum(x => x.Value);

        var bExpense = Sum(ValueKind.Budget, false);
        var fExpense = Sum(ValueKind.Forecast, false);
        var bIncome = Sum(ValueKind.Budget, true);
        var fIncome = Sum(ValueKind.Forecast, true);
        var bNet = bExpense - bIncome;
        var fNet = fExpense - fIncome;

        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYearId)
            .OrderBy(x => x.Sequence).Select(x => new { x.Id, x.Name, x.Sequence }).ToListAsync(cancellationToken);
        var monthly = periods.Select(period =>
        {
            var pf = facts.Where(x => x.PeriodId == period.Id).ToList();
            var be = Sum(ValueKind.Budget, false, pf); var fe = Sum(ValueKind.Forecast, false, pf);
            var bi = Sum(ValueKind.Budget, true, pf); var fi = Sum(ValueKind.Forecast, true, pf);
            return new ExpenseDashboardMonthlyDto(period.Id, period.Name, period.Sequence, be, fe, bi, fi, be - bi, fe - fi);
        }).ToList();

        var classRows = classMembers.Values.Select(member =>
        {
            var mf = facts.Where(x => ClassOf(x)?.Id == member.Id).ToList();
            var budget = mf.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value);
            var forecast = mf.Where(x => x.ValueKind == ValueKind.Forecast).Sum(x => x.Value);
            return new ExpenseDashboardClassRowDto(member.Id, member.Code, member.Name, budget, forecast, forecast - budget);
        }).Where(x => x.BudgetAmount != 0 || x.ForecastAmount != 0).OrderByDescending(x => x.ForecastAmount).ThenByDescending(x => x.BudgetAmount).ToList();
        var unclassifiedBudget = facts.Where(x => x.ValueKind == ValueKind.Budget && ClassOf(x) is null).Sum(x => x.Value);
        var unclassifiedForecast = facts.Where(x => x.ValueKind == ValueKind.Forecast && ClassOf(x) is null).Sum(x => x.Value);
        if (unclassifiedBudget != 0 || unclassifiedForecast != 0)
            classRows.Add(new ExpenseDashboardClassRowDto(Guid.Empty, "UNCLASSIFIED", "بدون طبقه‌بندی", unclassifiedBudget, unclassifiedForecast, unclassifiedForecast - unclassifiedBudget));

        var dimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == version.ModelId && x.Dimension!.IsActive && x.Dimension.Code != ClassCode)
            .OrderBy(x => x.Sequence)
            .Select(x => new DashboardDimensionOptionDto(x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence))
            .ToListAsync(cancellationToken);
        var selected = dimensionId.HasValue
            ? dimensions.FirstOrDefault(x => x.Id == dimensionId.Value) ?? throw new ArgumentException("Selected dimension is not available for expense drill-down.")
            : dimensions.FirstOrDefault(x => x.Code == "COSTCENTER") ?? dimensions.FirstOrDefault(x => x.Code == "DEPARTMENT") ?? dimensions.FirstOrDefault(x => x.Code == ItemCode) ?? dimensions.FirstOrDefault();

        var drilldown = selected is null ? [] : await BuildDrilldownAsync(companyId, selected.Id, facts, classDimension.Id, classMembers, bExpense, fExpense, bIncome, fIncome, Math.Clamp(take, 1, 500), cancellationToken);
        return new ExpenseDashboardDto(version.Id, version.VersionNumber, version.Name, currency,
            bExpense, fExpense, bIncome, fIncome, bNet, fNet, fNet - bNet, monthly, classRows, dimensions, selected?.Id, drilldown);
    }

    private async Task<IReadOnlyList<ExpenseDashboardDrilldownRowDto>> BuildDrilldownAsync(
        Guid companyId,
        Guid dimensionId,
        IReadOnlyList<BudgetFact> facts,
        Guid classDimensionId,
        IReadOnlyDictionary<Guid, ClassMember> classes,
        decimal totalBExpense,
        decimal totalFExpense,
        decimal totalBIncome,
        decimal totalFIncome,
        int take,
        CancellationToken ct)
    {
        var memberIds = facts.SelectMany(x => x.Dimensions.Where(d => d.DimensionId == dimensionId).Select(d => d.MemberId)).Distinct().ToArray();
        var members = await db.DimensionMembers.AsNoTracking().Where(x => memberIds.Contains(x.Id) && x.DimensionId == dimensionId && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.Id, x.Code, x.Name }).ToDictionaryAsync(x => x.Id, ct);
        bool Income(BudgetFact fact)
        {
            var classId = fact.Dimensions.Where(x => x.DimensionId == classDimensionId).Select(x => (Guid?)x.MemberId).SingleOrDefault();
            return classId.HasValue && classes.TryGetValue(classId.Value, out var member) && IncomeClasses.Contains(member.Code);
        }

        var rows = members.Values.Select(member =>
        {
            var mf = facts.Where(x => x.Dimensions.Any(d => d.DimensionId == dimensionId && d.MemberId == member.Id)).ToList();
            decimal S(ValueKind kind, bool income) => mf.Where(x => x.ValueKind == kind && Income(x) == income).Sum(x => x.Value);
            var be = S(ValueKind.Budget, false); var fe = S(ValueKind.Forecast, false);
            var bi = S(ValueKind.Budget, true); var fi = S(ValueKind.Forecast, true);
            return new ExpenseDashboardDrilldownRowDto(member.Id, member.Code, member.Name, be, fe, bi, fi, be - bi, fe - fi, (fe - fi) - (be - bi));
        }).Where(x => x.BudgetExpense != 0 || x.ForecastExpense != 0 || x.BudgetIncome != 0 || x.ForecastIncome != 0).ToList();

        var ube = totalBExpense - rows.Sum(x => x.BudgetExpense); var ufe = totalFExpense - rows.Sum(x => x.ForecastExpense);
        var ubi = totalBIncome - rows.Sum(x => x.BudgetIncome); var ufi = totalFIncome - rows.Sum(x => x.ForecastIncome);
        if (ube != 0 || ufe != 0 || ubi != 0 || ufi != 0)
            rows.Add(new ExpenseDashboardDrilldownRowDto(Guid.Empty, "UNALLOCATED", "بدون تفکیک", ube, ufe, ubi, ufi, ube - ubi, ufe - ufi, (ufe - ufi) - (ube - ubi)));
        return rows.OrderByDescending(x => x.ForecastNetCost).ThenByDescending(x => x.BudgetNetCost).Take(take).ToList();
    }

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct)) throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
    }
    private async Task<string> GetBaseCurrencyAsync(Guid tenantId, CancellationToken ct) =>
        await db.Currencies.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync(ct) ?? "IRR";
    private sealed record ClassMember(Guid Id, string Code, string Name);
}
