using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class SalesDashboardService(
    PbmDbContext db,
    IUserContext user,
    CommercialPlanningProvisioner provisioner) : ISalesDashboardService
{
    private const string ModelCode = "TRADE";
    private const string ProductDimensionCode = "PRODUCT";
    private const string PurchaseCostDimensionCode = "PURCHASECOST";

    public async Task<SalesDashboardDto?> GetAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid? dimensionId = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, cancellationToken);
        var tenantId = await db.Companies.AsNoTracking().Where(x => x.Id == companyId).Select(x => x.TenantId).SingleAsync(cancellationToken);
        await provisioner.EnsureSalesAsync(tenantId, cancellationToken);
        if (!await db.FiscalYears.AsNoTracking().AnyAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");

        var version = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.CompanyId == companyId
                && x.BudgetPlan.FiscalYearId == fiscalYearId
                && x.BudgetPlan.BudgetModel!.Code == ModelCode
                && x.Status != BudgetStatus.Rejected)
            .OrderByDescending(x => x.VersionNumber).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.Id, x.VersionNumber, x.Name, ModelId = x.BudgetPlan!.BudgetModelId })
            .FirstOrDefaultAsync(cancellationToken);
        if (version is null) return null;

        var codes = new[]
        {
            "SALES_QTY", "FREE_SALES_QTY", "GROSS_SALES", "SALES_DISCOUNT", "FOC_SALES_AMOUNT", "SALES_RETURN",
            "NET_SALES", "SALES_COGS_TOTAL", "PURCHASE_COMPANY_DISCOUNT"
        };
        var measures = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == version.ModelId && codes.Contains(x.Code))
            .Select(x => new MeasureRef(x.Id, x.Code, x.ValueType))
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        if (codes.Any(code => !measures.ContainsKey(code))) throw new InvalidOperationException("Sales dashboard measures are not fully initialized.");

        var currency = await GetBaseCurrencyAsync(tenantId, cancellationToken);
        var supportedKinds = new[] { ValueKind.Budget, ValueKind.Actual, ValueKind.Forecast };
        var measureIds = measures.Values.Select(x => x.Id).ToArray();
        var amountIds = measures.Values.Where(x => x.ValueType == MeasureValueType.Amount).Select(x => x.Id).ToHashSet();
        var facts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == version.Id && measureIds.Contains(x.MeasureId) && supportedKinds.Contains(x.ValueKind)
                && (!amountIds.Contains(x.MeasureId) || x.CurrencyCode == currency))
            .Select(x => new SalesFact(x.PeriodId, x.MeasureId, x.ValueKind, x.Value))
            .ToListAsync(cancellationToken);

        Guid Id(string code) => measures[code].Id;
        decimal Sum(string code, ValueKind kind, IReadOnlyList<SalesFact>? source = null) => (source ?? facts)
            .Where(x => x.MeasureId == Id(code) && x.ValueKind == kind).Sum(x => x.Value);
        SalesTotals Totals(ValueKind kind, IReadOnlyList<SalesFact>? source = null)
        {
            var gross = Sum("GROSS_SALES", kind, source);
            var discount = Sum("SALES_DISCOUNT", kind, source) + Sum("FOC_SALES_AMOUNT", kind, source);
            var salesReturn = Sum("SALES_RETURN", kind, source);
            var net = Sum("NET_SALES", kind, source);
            var cogs = Sum("SALES_COGS_TOTAL", kind, source);
            var companyDiscount = Sum("PURCHASE_COMPANY_DISCOUNT", kind, source);
            return new SalesTotals(
                Sum("SALES_QTY", kind, source),
                Sum("FREE_SALES_QTY", kind, source),
                gross,
                discount,
                salesReturn,
                net,
                cogs,
                companyDiscount,
                net - cogs + companyDiscount);
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
            return new SalesDashboardMonthlyDto(
                period.Id, period.Name, period.Sequence,
                b.Quantity, a.Quantity, f.Quantity,
                b.GrossSales, a.GrossSales, f.GrossSales,
                b.Discount, a.Discount, f.Discount,
                b.Return, a.Return, f.Return,
                b.NetSales, a.NetSales, f.NetSales,
                b.Cogs, a.Cogs, f.Cogs,
                b.GrossProfit, a.GrossProfit, f.GrossProfit);
        }).ToList();

        var dimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == version.ModelId && x.Dimension!.IsActive && x.Dimension.Code != PurchaseCostDimensionCode)
            .OrderBy(x => x.Sequence)
            .Select(x => new DashboardDimensionOptionDto(x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence))
            .ToListAsync(cancellationToken);
        var selected = dimensionId.HasValue
            ? dimensions.FirstOrDefault(x => x.Id == dimensionId.Value) ?? throw new ArgumentException("Selected dimension is not available for sales drill-down.")
            : dimensions.FirstOrDefault(x => x.Code == ProductDimensionCode) ?? dimensions.FirstOrDefault();
        var drilldown = selected is null ? [] : await BuildDrilldownAsync(
            version.Id, companyId, selected.Id, measures, currency, budget, actual, forecast,
            Math.Clamp(take, 1, 500), cancellationToken);

        return new SalesDashboardDto(
            version.Id, version.VersionNumber, version.Name, currency,
            budget.Quantity, actual.Quantity, forecast.Quantity,
            budget.FreeQuantity, actual.FreeQuantity, forecast.FreeQuantity,
            budget.GrossSales, actual.GrossSales, forecast.GrossSales,
            budget.Discount, actual.Discount, forecast.Discount,
            budget.Return, actual.Return, forecast.Return,
            budget.NetSales, actual.NetSales, forecast.NetSales,
            budget.Cogs, actual.Cogs, forecast.Cogs,
            budget.CompanyDiscount, actual.CompanyDiscount, forecast.CompanyDiscount,
            budget.GrossProfit, actual.GrossProfit, forecast.GrossProfit,
            actual.NetSales - budget.NetSales,
            forecast.NetSales - budget.NetSales,
            monthly, dimensions, selected?.Id, drilldown);
    }

    private async Task<IReadOnlyList<SalesDashboardDrilldownRowDto>> BuildDrilldownAsync(
        Guid versionId,
        Guid companyId,
        Guid dimensionId,
        IReadOnlyDictionary<string, MeasureRef> measures,
        string currency,
        SalesTotals totalBudget,
        SalesTotals totalActual,
        SalesTotals totalForecast,
        int take,
        CancellationToken ct)
    {
        var ids = measures.ToDictionary(x => x.Key, x => x.Value.Id, StringComparer.OrdinalIgnoreCase);
        var quantityId = ids["SALES_QTY"];
        var tracked = new[] { quantityId, ids["GROSS_SALES"], ids["NET_SALES"], ids["SALES_COGS_TOTAL"], ids["PURCHASE_COMPANY_DISCOUNT"] };
        var links = await db.BudgetFactDimensions.AsNoTracking()
            .Where(x => x.DimensionId == dimensionId
                && x.BudgetFact!.VersionId == versionId
                && tracked.Contains(x.BudgetFact.MeasureId)
                && (x.BudgetFact.ValueKind == ValueKind.Budget || x.BudgetFact.ValueKind == ValueKind.Actual || x.BudgetFact.ValueKind == ValueKind.Forecast)
                && (x.BudgetFact.MeasureId == quantityId || x.BudgetFact.CurrencyCode == currency))
            .Select(x => new { x.MemberId, x.BudgetFact!.MeasureId, x.BudgetFact.ValueKind, x.BudgetFact.Value })
            .ToListAsync(ct);
        var memberIds = links.Select(x => x.MemberId).Distinct().ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => memberIds.Contains(x.Id) && x.DimensionId == dimensionId && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.Id, x.Code, x.Name }).ToDictionaryAsync(x => x.Id, ct);

        var rows = links.Where(x => members.ContainsKey(x.MemberId)).GroupBy(x => x.MemberId).Select(group =>
        {
            var member = members[group.Key];
            decimal S(string code, ValueKind kind) => group.Where(x => x.MeasureId == ids[code] && x.ValueKind == kind).Sum(x => x.Value);
            SalesTotals T(ValueKind kind)
            {
                var net = S("NET_SALES", kind);
                var cogs = S("SALES_COGS_TOTAL", kind);
                var cd = S("PURCHASE_COMPANY_DISCOUNT", kind);
                return new SalesTotals(S("SALES_QTY", kind), 0, S("GROSS_SALES", kind), 0, 0, net, cogs, cd, net - cogs + cd);
            }
            var b = T(ValueKind.Budget); var a = T(ValueKind.Actual); var f = T(ValueKind.Forecast);
            return new SalesDashboardDrilldownRowDto(
                member.Id, member.Code, member.Name,
                b.Quantity, a.Quantity, f.Quantity,
                b.GrossSales, a.GrossSales, f.GrossSales,
                b.NetSales, a.NetSales, f.NetSales,
                b.Cogs, a.Cogs, f.Cogs,
                b.GrossProfit, a.GrossProfit, f.GrossProfit,
                a.NetSales - b.NetSales, f.NetSales - b.NetSales);
        }).ToList();

        SalesTotals Allocated(ValueKind kind) => new(
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetQuantity : kind == ValueKind.Actual ? x.ActualQuantity : x.ForecastQuantity),
            0,
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetGrossSales : kind == ValueKind.Actual ? x.ActualGrossSales : x.ForecastGrossSales),
            0,
            0,
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetNetSales : kind == ValueKind.Actual ? x.ActualNetSales : x.ForecastNetSales),
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetCogs : kind == ValueKind.Actual ? x.ActualCogs : x.ForecastCogs),
            links.Where(x => x.MeasureId == ids["PURCHASE_COMPANY_DISCOUNT"] && x.ValueKind == kind).Sum(x => x.Value),
            rows.Sum(x => kind == ValueKind.Budget ? x.BudgetGrossProfit : kind == ValueKind.Actual ? x.ActualGrossProfit : x.ForecastGrossProfit));
        SalesTotals Remaining(SalesTotals total, SalesTotals allocated) => new(
            total.Quantity - allocated.Quantity,
            0,
            total.GrossSales - allocated.GrossSales,
            0,
            0,
            total.NetSales - allocated.NetSales,
            total.Cogs - allocated.Cogs,
            total.CompanyDiscount - allocated.CompanyDiscount,
            total.GrossProfit - allocated.GrossProfit);

        var ub = Remaining(totalBudget, Allocated(ValueKind.Budget));
        var ua = Remaining(totalActual, Allocated(ValueKind.Actual));
        var uf = Remaining(totalForecast, Allocated(ValueKind.Forecast));
        if (HasValues(ub) || HasValues(ua) || HasValues(uf))
            rows.Add(new SalesDashboardDrilldownRowDto(
                Guid.Empty, "UNALLOCATED", "بدون تفکیک",
                ub.Quantity, ua.Quantity, uf.Quantity,
                ub.GrossSales, ua.GrossSales, uf.GrossSales,
                ub.NetSales, ua.NetSales, uf.NetSales,
                ub.Cogs, ua.Cogs, uf.Cogs,
                ub.GrossProfit, ua.GrossProfit, uf.GrossProfit,
                ua.NetSales - ub.NetSales, uf.NetSales - ub.NetSales));

        return rows.OrderByDescending(x => x.ActualNetSales).ThenByDescending(x => x.ForecastNetSales).ThenByDescending(x => x.BudgetNetSales).Take(take).ToList();
    }

    private static bool HasValues(SalesTotals x) => x.Quantity != 0 || x.GrossSales != 0 || x.NetSales != 0 || x.Cogs != 0 || x.CompanyDiscount != 0 || x.GrossProfit != 0;

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct))
            throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private async Task<string> GetBaseCurrencyAsync(Guid tenantId, CancellationToken ct) =>
        await db.Currencies.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync(ct) ?? "IRR";

    private sealed record MeasureRef(Guid Id, string Code, MeasureValueType ValueType);
    private sealed record SalesFact(Guid PeriodId, Guid MeasureId, ValueKind ValueKind, decimal Value);
    private sealed record SalesTotals(
        decimal Quantity,
        decimal FreeQuantity,
        decimal GrossSales,
        decimal Discount,
        decimal Return,
        decimal NetSales,
        decimal Cogs,
        decimal CompanyDiscount,
        decimal GrossProfit);
}
