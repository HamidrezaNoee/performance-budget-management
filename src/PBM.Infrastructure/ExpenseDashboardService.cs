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
                && (x.ValueKind == ValueKind.Budget || x.ValueKind == ValueKind.Actual || x.ValueKind == ValueKind.Forecast)
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
        ExpenseTotals Totals(ValueKind kind, IEnumerable<BudgetFact>? source = null)
        {
            var expense = Sum(kind, false, source);
            var income = Sum(kind, true, source);
            return new ExpenseTotals(expense, income, expense - income);
        }

        var budget = Totals(ValueKind.Budget);
        var actual = Totals(ValueKind.Actual);
        var forecast = Totals(ValueKind.Forecast);

        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYearId)
            .OrderBy(x => x.Sequence).Select(x => new { x.Id, x.Name, x.Sequence }).ToListAsync(cancellationToken);
        var monthly = periods.Select(period =>
        {
            var periodFacts = facts.Where(x => x.PeriodId == period.Id).ToList();
            var b = Totals(ValueKind.Budget, periodFacts);
            var a = Totals(ValueKind.Actual, periodFacts);
            var f = Totals(ValueKind.Forecast, periodFacts);
            return new ExpenseDashboardMonthlyDto(
                period.Id, period.Name, period.Sequence,
                b.Expense, a.Expense, f.Expense,
                b.Income, a.Income, f.Income,
                b.NetCost, a.NetCost, f.NetCost);
        }).ToList();

        var classRows = classMembers.Values.Select(member =>
        {
            var memberFacts = facts.Where(x => ClassOf(x)?.Id == member.Id).ToList();
            var b = memberFacts.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value);
            var a = memberFacts.Where(x => x.ValueKind == ValueKind.Actual).Sum(x => x.Value);
            var f = memberFacts.Where(x => x.ValueKind == ValueKind.Forecast).Sum(x => x.Value);
            return new ExpenseDashboardClassRowDto(member.Id, member.Code, member.Name, b, a, f, a - b, f - b);
        }).Where(x => x.BudgetAmount != 0 || x.ActualAmount != 0 || x.ForecastAmount != 0)
            .OrderByDescending(x => x.ActualAmount).ThenByDescending(x => x.ForecastAmount).ThenByDescending(x => x.BudgetAmount).ToList();
        var unclassifiedBudget = facts.Where(x => x.ValueKind == ValueKind.Budget && ClassOf(x) is null).Sum(x => x.Value);
        var unclassifiedActual = facts.Where(x => x.ValueKind == ValueKind.Actual && ClassOf(x) is null).Sum(x => x.Value);
        var unclassifiedForecast = facts.Where(x => x.ValueKind == ValueKind.Forecast && ClassOf(x) is null).Sum(x => x.Value);
        if (unclassifiedBudget != 0 || unclassifiedActual != 0 || unclassifiedForecast != 0)
            classRows.Add(new ExpenseDashboardClassRowDto(
                Guid.Empty, "UNCLASSIFIED", "بدون طبقه‌بندی",
                unclassifiedBudget, unclassifiedActual, unclassifiedForecast,
                unclassifiedActual - unclassifiedBudget, unclassifiedForecast - unclassifiedBudget));

        var dimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == version.ModelId && x.Dimension!.IsActive && x.Dimension.Code != ClassCode)
            .OrderBy(x => x.Sequence)
            .Select(x => new DashboardDimensionOptionDto(x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence))
            .ToListAsync(cancellationToken);
        var selected = dimensionId.HasValue
            ? dimensions.FirstOrDefault(x => x.Id == dimensionId.Value) ?? throw new ArgumentException("Selected dimension is not available for expense drill-down.")
            : dimensions.FirstOrDefault(x => x.Code == "COSTCENTER") ?? dimensions.FirstOrDefault(x => x.Code == "DEPARTMENT") ?? dimensions.FirstOrDefault(x => x.Code == ItemCode) ?? dimensions.FirstOrDefault();

        var drilldown = selected is null ? [] : await BuildDrilldownAsync(
            companyId, selected.Id, facts, classDimension.Id, classMembers,
            budget, actual, forecast, Math.Clamp(take, 1, 500), cancellationToken);
        return new ExpenseDashboardDto(
            version.Id, version.VersionNumber, version.Name, currency,
            budget.Expense, actual.Expense, forecast.Expense,
            budget.Income, actual.Income, forecast.Income,
            budget.NetCost, actual.NetCost, forecast.NetCost,
            actual.NetCost - budget.NetCost,
            forecast.NetCost - budget.NetCost,
            monthly, classRows, dimensions, selected?.Id, drilldown);
    }

    private async Task<IReadOnlyList<ExpenseDashboardDrilldownRowDto>> BuildDrilldownAsync(
        Guid companyId,
        Guid dimensionId,
        IReadOnlyList<BudgetFact> facts,
        Guid classDimensionId,
        IReadOnlyDictionary<Guid, ClassMember> classes,
        ExpenseTotals totalBudget,
        ExpenseTotals totalActual,
        ExpenseTotals totalForecast,
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
            var memberFacts = facts.Where(x => x.Dimensions.Any(d => d.DimensionId == dimensionId && d.MemberId == member.Id)).ToList();
            ExpenseTotals T(ValueKind kind)
            {
                var expense = memberFacts.Where(x => x.ValueKind == kind && !Income(x)).Sum(x => x.Value);
                var income = memberFacts.Where(x => x.ValueKind == kind && Income(x)).Sum(x => x.Value);
                return new ExpenseTotals(expense, income, expense - income);
            }
            var b = T(ValueKind.Budget); var a = T(ValueKind.Actual); var f = T(ValueKind.Forecast);
            return new ExpenseDashboardDrilldownRowDto(
                member.Id, member.Code, member.Name,
                b.Expense, a.Expense, f.Expense,
                b.Income, a.Income, f.Income,
                b.NetCost, a.NetCost, f.NetCost,
                a.NetCost - b.NetCost, f.NetCost - b.NetCost);
        }).Where(x => x.BudgetExpense != 0 || x.ActualExpense != 0 || x.ForecastExpense != 0 || x.BudgetIncome != 0 || x.ActualIncome != 0 || x.ForecastIncome != 0).ToList();

        ExpenseTotals Allocated(ValueKind kind) => new(
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetExpense : kind == ValueKind.Actual ? x.ActualExpense : x.ForecastExpense),
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetIncome : kind == ValueKind.Actual ? x.ActualIncome : x.ForecastIncome),
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetNetCost : kind == ValueKind.Actual ? x.ActualNetCost : x.ForecastNetCost));
        ExpenseTotals Remaining(ExpenseTotals total, ExpenseTotals allocated) => new(
            total.Expense - allocated.Expense,
            total.Income - allocated.Income,
            total.NetCost - allocated.NetCost);

        var ub = Remaining(totalBudget, Allocated(ValueKind.Budget));
        var ua = Remaining(totalActual, Allocated(ValueKind.Actual));
        var uf = Remaining(totalForecast, Allocated(ValueKind.Forecast));
        if (HasValues(ub) || HasValues(ua) || HasValues(uf))
            rows.Add(new ExpenseDashboardDrilldownRowDto(
                Guid.Empty, "UNALLOCATED", "بدون تفکیک",
                ub.Expense, ua.Expense, uf.Expense,
                ub.Income, ua.Income, uf.Income,
                ub.NetCost, ua.NetCost, uf.NetCost,
                ua.NetCost - ub.NetCost, uf.NetCost - ub.NetCost));
        return rows.OrderByDescending(x => x.ActualNetCost).ThenByDescending(x => x.ForecastNetCost).ThenByDescending(x => x.BudgetNetCost).Take(take).ToList();
    }

    private static bool HasValues(ExpenseTotals x) => x.Expense != 0 || x.Income != 0 || x.NetCost != 0;

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct)) throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
    }
    private async Task<string> GetBaseCurrencyAsync(Guid tenantId, CancellationToken ct) =>
        await db.Currencies.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync(ct) ?? "IRR";
    private sealed record ClassMember(Guid Id, string Code, string Name);
    private sealed record ExpenseTotals(decimal Expense, decimal Income, decimal NetCost);
}
