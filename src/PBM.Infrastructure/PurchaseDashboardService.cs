using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class PurchaseDashboardService(PbmDbContext db, IUserContext user) : IPurchaseDashboardService
{
    private const string TradeModelCode = "TRADE";
    private const string ProductDimensionCode = "PRODUCT";
    private const string CostDimensionCode = "PURCHASECOST";
    private const string QuantityMeasureCode = "PURCHASE_FORECAST_QTY";
    private const string AmountMeasureCode = "PURCHASE_FORECAST_AMOUNT";
    private const string CostAmountMeasureCode = "PURCHASE_COST_AMOUNT";

    public async Task<PurchaseDashboardDto?> GetAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid? dimensionId = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, cancellationToken);
        if (!await db.FiscalYears.AsNoTracking().AnyAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");

        var version = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.CompanyId == companyId
                && x.BudgetPlan.FiscalYearId == fiscalYearId
                && x.BudgetPlan.BudgetModel!.Code == TradeModelCode
                && x.Status != BudgetStatus.Rejected)
            .OrderByDescending(x => x.VersionNumber).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.Id, x.VersionNumber, x.Name, ModelId = x.BudgetPlan!.BudgetModelId })
            .FirstOrDefaultAsync(cancellationToken);
        if (version is null) return null;

        var measureCodes = new[] { QuantityMeasureCode, AmountMeasureCode, CostAmountMeasureCode };
        var measures = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == version.ModelId && measureCodes.Contains(x.Code))
            .Select(x => new { x.Id, x.Code })
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        if (measureCodes.Any(code => !measures.ContainsKey(code)))
            throw new InvalidOperationException("Purchase planning measures are not fully initialized.");

        var quantityMeasureId = measures[QuantityMeasureCode].Id;
        var amountMeasureId = measures[AmountMeasureCode].Id;
        var costMeasureId = measures[CostAmountMeasureCode].Id;
        var trackedMeasureIds = new[] { quantityMeasureId, amountMeasureId, costMeasureId };
        var currency = await GetBaseCurrencyAsync(cancellationToken);
        var supportedKinds = new[] { ValueKind.Budget, ValueKind.Actual, ValueKind.Forecast };

        var facts = await db.BudgetFacts.AsNoTracking().Include(x => x.Dimensions)
            .Where(x => x.VersionId == version.Id
                && trackedMeasureIds.Contains(x.MeasureId)
                && supportedKinds.Contains(x.ValueKind)
                && (x.MeasureId == quantityMeasureId || x.CurrencyCode == currency))
            .ToListAsync(cancellationToken);

        PurchaseTotals Totals(ValueKind kind, IEnumerable<BudgetFact>? source = null)
        {
            var items = (source ?? facts).Where(x => x.ValueKind == kind).ToList();
            var quantity = items.Where(x => x.MeasureId == quantityMeasureId).Sum(x => x.Value);
            var purchase = items.Where(x => x.MeasureId == amountMeasureId).Sum(x => x.Value);
            var cost = items.Where(x => x.MeasureId == costMeasureId).Sum(x => x.Value);
            return new PurchaseTotals(quantity, purchase, cost, purchase + cost);
        }

        var budget = Totals(ValueKind.Budget);
        var actual = Totals(ValueKind.Actual);
        var forecast = Totals(ValueKind.Forecast);

        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == fiscalYearId)
            .OrderBy(x => x.Sequence)
            .Select(x => new { x.Id, x.Name, x.Sequence })
            .ToListAsync(cancellationToken);
        var monthly = periods.Select(period =>
        {
            var periodFacts = facts.Where(x => x.PeriodId == period.Id).ToList();
            var b = Totals(ValueKind.Budget, periodFacts);
            var a = Totals(ValueKind.Actual, periodFacts);
            var f = Totals(ValueKind.Forecast, periodFacts);
            return new PurchaseDashboardMonthlyDto(
                period.Id, period.Name, period.Sequence,
                b.Quantity, a.Quantity, f.Quantity,
                b.PurchaseAmount, a.PurchaseAmount, f.PurchaseAmount,
                b.CostAmount, a.CostAmount, f.CostAmount,
                b.TotalAmount, a.TotalAmount, f.TotalAmount);
        }).ToList();

        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == version.ModelId && x.Dimension!.IsActive)
            .OrderBy(x => x.Sequence)
            .Select(x => new DashboardDimensionOptionDto(x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence))
            .ToListAsync(cancellationToken);
        var costDimension = modelDimensions.FirstOrDefault(x => x.Code == CostDimensionCode);
        var drillDimensions = modelDimensions.Where(x => x.Code != CostDimensionCode).ToList();

        var costs = costDimension is null
            ? BuildUnallocatedCostRows([], budget.CostAmount, actual.CostAmount, forecast.CostAmount)
            : await BuildCostBreakdownAsync(
                companyId, costDimension.Id, costMeasureId, facts,
                budget.CostAmount, actual.CostAmount, forecast.CostAmount, cancellationToken);

        var selectedDimension = dimensionId.HasValue
            ? drillDimensions.FirstOrDefault(x => x.Id == dimensionId.Value)
                ?? throw new ArgumentException("Selected dimension is not available for purchase dashboard drill-down.")
            : drillDimensions.FirstOrDefault(x => x.Code == ProductDimensionCode) ?? drillDimensions.FirstOrDefault();

        var drilldown = selectedDimension is null
            ? []
            : await BuildDrilldownAsync(
                companyId, selectedDimension.Id, facts,
                quantityMeasureId, amountMeasureId, costMeasureId,
                budget, actual, forecast, Math.Clamp(take, 1, 500), cancellationToken);

        return new PurchaseDashboardDto(
            version.Id, version.VersionNumber, version.Name, currency,
            budget.Quantity, actual.Quantity, forecast.Quantity,
            budget.PurchaseAmount, actual.PurchaseAmount, forecast.PurchaseAmount,
            budget.CostAmount, actual.CostAmount, forecast.CostAmount,
            budget.TotalAmount, actual.TotalAmount, forecast.TotalAmount,
            actual.TotalAmount - budget.TotalAmount,
            forecast.TotalAmount - budget.TotalAmount,
            monthly, costs, drillDimensions, selectedDimension?.Id, drilldown);
    }

    private async Task<IReadOnlyList<PurchaseDashboardCostDto>> BuildCostBreakdownAsync(
        Guid companyId,
        Guid costDimensionId,
        Guid costMeasureId,
        IReadOnlyList<BudgetFact> facts,
        decimal totalBudgetCost,
        decimal totalActualCost,
        decimal totalForecastCost,
        CancellationToken ct)
    {
        var costFacts = facts.Where(x => x.MeasureId == costMeasureId).ToList();
        var memberIds = costFacts.SelectMany(x => x.Dimensions
            .Where(d => d.DimensionId == costDimensionId).Select(d => d.MemberId)).Distinct().ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => memberIds.Contains(x.Id) && x.DimensionId == costDimensionId && x.IsActive
                && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.Id, x.Code, x.Name }).ToDictionaryAsync(x => x.Id, ct);

        var rows = members.Values.Select(member =>
        {
            var memberFacts = costFacts.Where(x => x.Dimensions.Any(d => d.DimensionId == costDimensionId && d.MemberId == member.Id)).ToList();
            var b = memberFacts.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value);
            var a = memberFacts.Where(x => x.ValueKind == ValueKind.Actual).Sum(x => x.Value);
            var f = memberFacts.Where(x => x.ValueKind == ValueKind.Forecast).Sum(x => x.Value);
            return new PurchaseDashboardCostDto(member.Id, member.Code, member.Name, b, a, f, a - b, f - b);
        }).Where(x => x.BudgetAmount != 0 || x.ActualAmount != 0 || x.ForecastAmount != 0).ToList();

        return BuildUnallocatedCostRows(rows, totalBudgetCost, totalActualCost, totalForecastCost);
    }

    private static IReadOnlyList<PurchaseDashboardCostDto> BuildUnallocatedCostRows(
        IReadOnlyList<PurchaseDashboardCostDto> rows,
        decimal totalBudgetCost,
        decimal totalActualCost,
        decimal totalForecastCost)
    {
        var result = rows.ToList();
        var ub = totalBudgetCost - result.Sum(x => x.BudgetAmount);
        var ua = totalActualCost - result.Sum(x => x.ActualAmount);
        var uf = totalForecastCost - result.Sum(x => x.ForecastAmount);
        if (ub != 0 || ua != 0 || uf != 0)
            result.Add(new PurchaseDashboardCostDto(Guid.Empty, "UNALLOCATED", "بدون نوع هزینه", ub, ua, uf, ua - ub, uf - ub));
        return result.OrderByDescending(x => x.ActualAmount).ThenByDescending(x => x.ForecastAmount).ThenByDescending(x => x.BudgetAmount).ToList();
    }

    private async Task<IReadOnlyList<PurchaseDashboardDrilldownRowDto>> BuildDrilldownAsync(
        Guid companyId,
        Guid dimensionId,
        IReadOnlyList<BudgetFact> facts,
        Guid quantityMeasureId,
        Guid amountMeasureId,
        Guid costMeasureId,
        PurchaseTotals totalBudget,
        PurchaseTotals totalActual,
        PurchaseTotals totalForecast,
        int take,
        CancellationToken ct)
    {
        var memberIds = facts.SelectMany(x => x.Dimensions.Where(d => d.DimensionId == dimensionId).Select(d => d.MemberId)).Distinct().ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => memberIds.Contains(x.Id) && x.DimensionId == dimensionId && x.IsActive
                && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.Id, x.Code, x.Name }).ToDictionaryAsync(x => x.Id, ct);

        PurchaseTotals T(IEnumerable<BudgetFact> source, ValueKind kind)
        {
            var items = source.Where(x => x.ValueKind == kind).ToList();
            var q = items.Where(x => x.MeasureId == quantityMeasureId).Sum(x => x.Value);
            var p = items.Where(x => x.MeasureId == amountMeasureId).Sum(x => x.Value);
            var c = items.Where(x => x.MeasureId == costMeasureId).Sum(x => x.Value);
            return new PurchaseTotals(q, p, c, p + c);
        }

        var rows = members.Values.Select(member =>
        {
            var memberFacts = facts.Where(x => x.Dimensions.Any(d => d.DimensionId == dimensionId && d.MemberId == member.Id)).ToList();
            var b = T(memberFacts, ValueKind.Budget); var a = T(memberFacts, ValueKind.Actual); var f = T(memberFacts, ValueKind.Forecast);
            return new PurchaseDashboardDrilldownRowDto(
                member.Id, member.Code, member.Name,
                b.Quantity, a.Quantity, f.Quantity,
                b.PurchaseAmount, a.PurchaseAmount, f.PurchaseAmount,
                b.CostAmount, a.CostAmount, f.CostAmount,
                b.TotalAmount, a.TotalAmount, f.TotalAmount,
                a.TotalAmount - b.TotalAmount,
                f.TotalAmount - b.TotalAmount);
        }).Where(x => x.BudgetTotalAmount != 0 || x.ActualTotalAmount != 0 || x.ForecastTotalAmount != 0
            || x.BudgetQuantity != 0 || x.ActualQuantity != 0 || x.ForecastQuantity != 0).ToList();

        PurchaseTotals Allocated(ValueKind kind) => new(
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetQuantity : kind == ValueKind.Actual ? x.ActualQuantity : x.ForecastQuantity),
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetPurchaseAmount : kind == ValueKind.Actual ? x.ActualPurchaseAmount : x.ForecastPurchaseAmount),
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetCostAmount : kind == ValueKind.Actual ? x.ActualCostAmount : x.ForecastCostAmount),
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetTotalAmount : kind == ValueKind.Actual ? x.ActualTotalAmount : x.ForecastTotalAmount));
        PurchaseTotals Remaining(PurchaseTotals total, PurchaseTotals allocated) => new(
            total.Quantity - allocated.Quantity,
            total.PurchaseAmount - allocated.PurchaseAmount,
            total.CostAmount - allocated.CostAmount,
            total.TotalAmount - allocated.TotalAmount);

        var ub = Remaining(totalBudget, Allocated(ValueKind.Budget));
        var ua = Remaining(totalActual, Allocated(ValueKind.Actual));
        var uf = Remaining(totalForecast, Allocated(ValueKind.Forecast));
        if (HasValues(ub) || HasValues(ua) || HasValues(uf))
            rows.Add(new PurchaseDashboardDrilldownRowDto(
                Guid.Empty, "UNALLOCATED", "بدون تفکیک",
                ub.Quantity, ua.Quantity, uf.Quantity,
                ub.PurchaseAmount, ua.PurchaseAmount, uf.PurchaseAmount,
                ub.CostAmount, ua.CostAmount, uf.CostAmount,
                ub.TotalAmount, ua.TotalAmount, uf.TotalAmount,
                ua.TotalAmount - ub.TotalAmount,
                uf.TotalAmount - ub.TotalAmount));

        return rows.OrderByDescending(x => x.ActualTotalAmount).ThenByDescending(x => x.ForecastTotalAmount).ThenByDescending(x => x.BudgetTotalAmount).Take(take).ToList();
    }

    private static bool HasValues(PurchaseTotals x) => x.Quantity != 0 || x.PurchaseAmount != 0 || x.CostAmount != 0 || x.TotalAmount != 0;

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct))
            throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private async Task<string> GetBaseCurrencyAsync(CancellationToken ct) =>
        await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsBaseCurrency && x.IsActive)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(ct) ?? "IRR";

    private sealed record PurchaseTotals(decimal Quantity, decimal PurchaseAmount, decimal CostAmount, decimal TotalAmount);
}
