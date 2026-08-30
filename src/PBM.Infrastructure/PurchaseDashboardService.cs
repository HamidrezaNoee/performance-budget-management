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
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new
            {
                x.Id,
                x.VersionNumber,
                x.Name,
                ModelId = x.BudgetPlan!.BudgetModelId
            })
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
        var currency = await GetBaseCurrencyAsync(cancellationToken);
        var supportedKinds = new[] { ValueKind.Budget, ValueKind.Forecast };

        var quantityFacts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == version.Id
                && x.MeasureId == quantityMeasureId
                && supportedKinds.Contains(x.ValueKind))
            .Select(x => new PurchaseFact(x.Id, x.PeriodId, x.MeasureId, x.ValueKind, x.Value))
            .ToListAsync(cancellationToken);

        var amountMeasureIds = new[] { amountMeasureId, costMeasureId };
        var amountFacts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == version.Id
                && amountMeasureIds.Contains(x.MeasureId)
                && supportedKinds.Contains(x.ValueKind)
                && x.CurrencyCode == currency)
            .Select(x => new PurchaseFact(x.Id, x.PeriodId, x.MeasureId, x.ValueKind, x.Value))
            .ToListAsync(cancellationToken);

        var allFacts = quantityFacts.Concat(amountFacts).ToList();
        decimal Sum(Guid measureId, ValueKind kind) => allFacts
            .Where(x => x.MeasureId == measureId && x.ValueKind == kind)
            .Sum(x => x.Value);

        var budgetQuantity = Sum(quantityMeasureId, ValueKind.Budget);
        var forecastQuantity = Sum(quantityMeasureId, ValueKind.Forecast);
        var budgetPurchaseAmount = Sum(amountMeasureId, ValueKind.Budget);
        var forecastPurchaseAmount = Sum(amountMeasureId, ValueKind.Forecast);
        var budgetCostAmount = Sum(costMeasureId, ValueKind.Budget);
        var forecastCostAmount = Sum(costMeasureId, ValueKind.Forecast);
        var budgetTotalAmount = budgetPurchaseAmount + budgetCostAmount;
        var forecastTotalAmount = forecastPurchaseAmount + forecastCostAmount;

        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == fiscalYearId)
            .OrderBy(x => x.Sequence)
            .Select(x => new { x.Id, x.Name, x.Sequence })
            .ToListAsync(cancellationToken);

        var monthly = periods.Select(period =>
        {
            var periodFacts = allFacts.Where(x => x.PeriodId == period.Id).ToList();
            decimal PeriodSum(Guid measureId, ValueKind kind) => periodFacts
                .Where(x => x.MeasureId == measureId && x.ValueKind == kind)
                .Sum(x => x.Value);
            var budgetPurchase = PeriodSum(amountMeasureId, ValueKind.Budget);
            var forecastPurchase = PeriodSum(amountMeasureId, ValueKind.Forecast);
            var budgetCost = PeriodSum(costMeasureId, ValueKind.Budget);
            var forecastCost = PeriodSum(costMeasureId, ValueKind.Forecast);
            return new PurchaseDashboardMonthlyDto(
                period.Id,
                period.Name,
                period.Sequence,
                PeriodSum(quantityMeasureId, ValueKind.Budget),
                PeriodSum(quantityMeasureId, ValueKind.Forecast),
                budgetPurchase,
                forecastPurchase,
                budgetCost,
                forecastCost,
                budgetPurchase + budgetCost,
                forecastPurchase + forecastCost);
        }).ToList();

        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == version.ModelId && x.Dimension!.IsActive)
            .OrderBy(x => x.Sequence)
            .Select(x => new DashboardDimensionOptionDto(x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence))
            .ToListAsync(cancellationToken);
        var costDimension = modelDimensions.FirstOrDefault(x => x.Code == CostDimensionCode);
        var drillDimensions = modelDimensions.Where(x => x.Code != CostDimensionCode).ToList();

        IReadOnlyList<PurchaseDashboardCostDto> costs = costDimension is null
            ? BuildUnallocatedCostRows(Array.Empty<PurchaseDashboardCostDto>(), budgetCostAmount, forecastCostAmount)
            : await BuildCostBreakdownAsync(
                version.Id,
                companyId,
                costDimension.Id,
                costMeasureId,
                currency,
                budgetCostAmount,
                forecastCostAmount,
                cancellationToken);

        DashboardDimensionOptionDto? selectedDimension = null;
        if (dimensionId.HasValue)
        {
            selectedDimension = drillDimensions.FirstOrDefault(x => x.Id == dimensionId.Value)
                ?? throw new ArgumentException("Selected dimension is not available for purchase dashboard drill-down.");
        }
        else
        {
            selectedDimension = drillDimensions.FirstOrDefault(x => x.Code == ProductDimensionCode)
                ?? drillDimensions.FirstOrDefault();
        }

        IReadOnlyList<PurchaseDashboardDrilldownRowDto> drilldown = selectedDimension is null
            ? Array.Empty<PurchaseDashboardDrilldownRowDto>()
            : await BuildDrilldownAsync(
                version.Id,
                companyId,
                selectedDimension.Id,
                quantityMeasureId,
                amountMeasureId,
                costMeasureId,
                currency,
                budgetQuantity,
                forecastQuantity,
                budgetPurchaseAmount,
                forecastPurchaseAmount,
                budgetCostAmount,
                forecastCostAmount,
                Math.Clamp(take, 1, 500),
                cancellationToken);

        return new PurchaseDashboardDto(
            version.Id,
            version.VersionNumber,
            version.Name,
            currency,
            budgetQuantity,
            forecastQuantity,
            budgetPurchaseAmount,
            forecastPurchaseAmount,
            budgetCostAmount,
            forecastCostAmount,
            budgetTotalAmount,
            forecastTotalAmount,
            forecastTotalAmount - budgetTotalAmount,
            monthly,
            costs,
            drillDimensions,
            selectedDimension?.Id,
            drilldown);
    }

    private async Task<IReadOnlyList<PurchaseDashboardCostDto>> BuildCostBreakdownAsync(
        Guid versionId,
        Guid companyId,
        Guid costDimensionId,
        Guid costMeasureId,
        string currency,
        decimal totalBudgetCost,
        decimal totalForecastCost,
        CancellationToken ct)
    {
        var links = await db.BudgetFactDimensions.AsNoTracking()
            .Where(x => x.DimensionId == costDimensionId
                && x.BudgetFact!.VersionId == versionId
                && x.BudgetFact.MeasureId == costMeasureId
                && (x.BudgetFact.ValueKind == ValueKind.Budget || x.BudgetFact.ValueKind == ValueKind.Forecast)
                && x.BudgetFact.CurrencyCode == currency)
            .Select(x => new { x.MemberId, x.BudgetFact!.ValueKind, x.BudgetFact.Value })
            .ToListAsync(ct);

        var memberIds = links.Select(x => x.MemberId).Distinct().ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => memberIds.Contains(x.Id)
                && x.DimensionId == costDimensionId
                && x.IsActive
                && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, ct);

        var rows = links
            .Where(x => members.ContainsKey(x.MemberId))
            .GroupBy(x => x.MemberId)
            .Select(group =>
            {
                var member = members[group.Key];
                var budget = group.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value);
                var forecast = group.Where(x => x.ValueKind == ValueKind.Forecast).Sum(x => x.Value);
                return new PurchaseDashboardCostDto(member.Id, member.Code, member.Name, budget, forecast, forecast - budget);
            })
            .OrderByDescending(x => x.ForecastAmount)
            .ThenByDescending(x => x.BudgetAmount)
            .ToList();

        return BuildUnallocatedCostRows(rows, totalBudgetCost, totalForecastCost);
    }

    private static IReadOnlyList<PurchaseDashboardCostDto> BuildUnallocatedCostRows(
        IReadOnlyList<PurchaseDashboardCostDto> rows,
        decimal totalBudgetCost,
        decimal totalForecastCost)
    {
        var result = rows.ToList();
        var allocatedBudget = result.Sum(x => x.BudgetAmount);
        var allocatedForecast = result.Sum(x => x.ForecastAmount);
        var unallocatedBudget = totalBudgetCost - allocatedBudget;
        var unallocatedForecast = totalForecastCost - allocatedForecast;
        if (unallocatedBudget != 0m || unallocatedForecast != 0m)
            result.Add(new PurchaseDashboardCostDto(
                Guid.Empty,
                "UNALLOCATED",
                "بدون نوع هزینه",
                unallocatedBudget,
                unallocatedForecast,
                unallocatedForecast - unallocatedBudget));
        return result
            .OrderByDescending(x => x.ForecastAmount)
            .ThenByDescending(x => x.BudgetAmount)
            .ToList();
    }

    private async Task<IReadOnlyList<PurchaseDashboardDrilldownRowDto>> BuildDrilldownAsync(
        Guid versionId,
        Guid companyId,
        Guid dimensionId,
        Guid quantityMeasureId,
        Guid amountMeasureId,
        Guid costMeasureId,
        string currency,
        decimal totalBudgetQuantity,
        decimal totalForecastQuantity,
        decimal totalBudgetPurchase,
        decimal totalForecastPurchase,
        decimal totalBudgetCost,
        decimal totalForecastCost,
        int take,
        CancellationToken ct)
    {
        var measureIds = new[] { quantityMeasureId, amountMeasureId, costMeasureId };
        var links = await db.BudgetFactDimensions.AsNoTracking()
            .Where(x => x.DimensionId == dimensionId
                && x.BudgetFact!.VersionId == versionId
                && measureIds.Contains(x.BudgetFact.MeasureId)
                && (x.BudgetFact.ValueKind == ValueKind.Budget || x.BudgetFact.ValueKind == ValueKind.Forecast)
                && (x.BudgetFact.MeasureId == quantityMeasureId || x.BudgetFact.CurrencyCode == currency))
            .Select(x => new
            {
                x.MemberId,
                x.BudgetFact!.MeasureId,
                x.BudgetFact.ValueKind,
                x.BudgetFact.Value
            })
            .ToListAsync(ct);

        var memberIds = links.Select(x => x.MemberId).Distinct().ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => memberIds.Contains(x.Id)
                && x.DimensionId == dimensionId
                && x.IsActive
                && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, ct);

        var rows = links
            .Where(x => members.ContainsKey(x.MemberId))
            .GroupBy(x => x.MemberId)
            .Select(group =>
            {
                var member = members[group.Key];
                decimal Sum(Guid measureId, ValueKind kind) => group
                    .Where(x => x.MeasureId == measureId && x.ValueKind == kind)
                    .Sum(x => x.Value);
                var budgetPurchase = Sum(amountMeasureId, ValueKind.Budget);
                var forecastPurchase = Sum(amountMeasureId, ValueKind.Forecast);
                var budgetCost = Sum(costMeasureId, ValueKind.Budget);
                var forecastCost = Sum(costMeasureId, ValueKind.Forecast);
                var budgetTotal = budgetPurchase + budgetCost;
                var forecastTotal = forecastPurchase + forecastCost;
                return new PurchaseDashboardDrilldownRowDto(
                    member.Id,
                    member.Code,
                    member.Name,
                    Sum(quantityMeasureId, ValueKind.Budget),
                    Sum(quantityMeasureId, ValueKind.Forecast),
                    budgetPurchase,
                    forecastPurchase,
                    budgetCost,
                    forecastCost,
                    budgetTotal,
                    forecastTotal,
                    forecastTotal - budgetTotal);
            })
            .ToList();

        var allocatedBudgetQuantity = rows.Sum(x => x.BudgetQuantity);
        var allocatedForecastQuantity = rows.Sum(x => x.ForecastQuantity);
        var allocatedBudgetPurchase = rows.Sum(x => x.BudgetPurchaseAmount);
        var allocatedForecastPurchase = rows.Sum(x => x.ForecastPurchaseAmount);
        var allocatedBudgetCost = rows.Sum(x => x.BudgetCostAmount);
        var allocatedForecastCost = rows.Sum(x => x.ForecastCostAmount);

        var unallocatedBudgetQuantity = totalBudgetQuantity - allocatedBudgetQuantity;
        var unallocatedForecastQuantity = totalForecastQuantity - allocatedForecastQuantity;
        var unallocatedBudgetPurchase = totalBudgetPurchase - allocatedBudgetPurchase;
        var unallocatedForecastPurchase = totalForecastPurchase - allocatedForecastPurchase;
        var unallocatedBudgetCost = totalBudgetCost - allocatedBudgetCost;
        var unallocatedForecastCost = totalForecastCost - allocatedForecastCost;
        if (unallocatedBudgetQuantity != 0m
            || unallocatedForecastQuantity != 0m
            || unallocatedBudgetPurchase != 0m
            || unallocatedForecastPurchase != 0m
            || unallocatedBudgetCost != 0m
            || unallocatedForecastCost != 0m)
        {
            var budgetTotal = unallocatedBudgetPurchase + unallocatedBudgetCost;
            var forecastTotal = unallocatedForecastPurchase + unallocatedForecastCost;
            rows.Add(new PurchaseDashboardDrilldownRowDto(
                Guid.Empty,
                "UNALLOCATED",
                "بدون تفکیک",
                unallocatedBudgetQuantity,
                unallocatedForecastQuantity,
                unallocatedBudgetPurchase,
                unallocatedForecastPurchase,
                unallocatedBudgetCost,
                unallocatedForecastCost,
                budgetTotal,
                forecastTotal,
                forecastTotal - budgetTotal));
        }

        return rows
            .OrderByDescending(x => x.ForecastTotalAmount)
            .ThenByDescending(x => x.BudgetTotalAmount)
            .Take(take)
            .ToList();
    }

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

    private sealed record PurchaseFact(
        Guid Id,
        Guid PeriodId,
        Guid MeasureId,
        ValueKind ValueKind,
        decimal Value);
}
