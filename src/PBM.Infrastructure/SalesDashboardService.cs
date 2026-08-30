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
        var supportedKinds = new[] { ValueKind.Budget, ValueKind.Forecast };
        var measureIds = measures.Values.Select(x => x.Id).ToArray();
        var amountIds = measures.Values.Where(x => x.ValueType == MeasureValueType.Amount).Select(x => x.Id).ToHashSet();
        var facts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == version.Id && measureIds.Contains(x.MeasureId) && supportedKinds.Contains(x.ValueKind)
                && (!amountIds.Contains(x.MeasureId) || x.CurrencyCode == currency))
            .Select(x => new SalesFact(x.Id, x.PeriodId, x.MeasureId, x.ValueKind, x.Value))
            .ToListAsync(cancellationToken);

        Guid Id(string code) => measures[code].Id;
        decimal Sum(string code, ValueKind kind, IReadOnlyList<SalesFact>? source = null) => (source ?? facts)
            .Where(x => x.MeasureId == Id(code) && x.ValueKind == kind).Sum(x => x.Value);

        var bQty = Sum("SALES_QTY", ValueKind.Budget);
        var fQty = Sum("SALES_QTY", ValueKind.Forecast);
        var bFree = Sum("FREE_SALES_QTY", ValueKind.Budget);
        var fFree = Sum("FREE_SALES_QTY", ValueKind.Forecast);
        var bGross = Sum("GROSS_SALES", ValueKind.Budget);
        var fGross = Sum("GROSS_SALES", ValueKind.Forecast);
        var bDiscount = Sum("SALES_DISCOUNT", ValueKind.Budget) + Sum("FOC_SALES_AMOUNT", ValueKind.Budget);
        var fDiscount = Sum("SALES_DISCOUNT", ValueKind.Forecast) + Sum("FOC_SALES_AMOUNT", ValueKind.Forecast);
        var bReturn = Sum("SALES_RETURN", ValueKind.Budget);
        var fReturn = Sum("SALES_RETURN", ValueKind.Forecast);
        var bNet = Sum("NET_SALES", ValueKind.Budget);
        var fNet = Sum("NET_SALES", ValueKind.Forecast);
        var bCogs = Sum("SALES_COGS_TOTAL", ValueKind.Budget);
        var fCogs = Sum("SALES_COGS_TOTAL", ValueKind.Forecast);
        var bCompanyDiscount = Sum("PURCHASE_COMPANY_DISCOUNT", ValueKind.Budget);
        var fCompanyDiscount = Sum("PURCHASE_COMPANY_DISCOUNT", ValueKind.Forecast);
        var bProfit = bNet - bCogs + bCompanyDiscount;
        var fProfit = fNet - fCogs + fCompanyDiscount;

        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYearId)
            .OrderBy(x => x.Sequence).Select(x => new { x.Id, x.Name, x.Sequence }).ToListAsync(cancellationToken);
        var monthly = periods.Select(period =>
        {
            var pf = facts.Where(x => x.PeriodId == period.Id).ToList();
            decimal PSum(string code, ValueKind kind) => Sum(code, kind, pf);
            var bg = PSum("GROSS_SALES", ValueKind.Budget);
            var fg = PSum("GROSS_SALES", ValueKind.Forecast);
            var bd = PSum("SALES_DISCOUNT", ValueKind.Budget) + PSum("FOC_SALES_AMOUNT", ValueKind.Budget);
            var fd = PSum("SALES_DISCOUNT", ValueKind.Forecast) + PSum("FOC_SALES_AMOUNT", ValueKind.Forecast);
            var br = PSum("SALES_RETURN", ValueKind.Budget);
            var fr = PSum("SALES_RETURN", ValueKind.Forecast);
            var bn = PSum("NET_SALES", ValueKind.Budget);
            var fn = PSum("NET_SALES", ValueKind.Forecast);
            var bc = PSum("SALES_COGS_TOTAL", ValueKind.Budget);
            var fc = PSum("SALES_COGS_TOTAL", ValueKind.Forecast);
            var bcd = PSum("PURCHASE_COMPANY_DISCOUNT", ValueKind.Budget);
            var fcd = PSum("PURCHASE_COMPANY_DISCOUNT", ValueKind.Forecast);
            return new SalesDashboardMonthlyDto(period.Id, period.Name, period.Sequence,
                PSum("SALES_QTY", ValueKind.Budget), PSum("SALES_QTY", ValueKind.Forecast),
                bg, fg, bd, fd, br, fr, bn, fn, bc, fc, bn - bc + bcd, fn - fc + fcd);
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
            version.Id, companyId, selected.Id, measures, currency,
            bQty, fQty, bGross, fGross, bNet, fNet, bCogs, fCogs, bCompanyDiscount, fCompanyDiscount,
            Math.Clamp(take, 1, 500), cancellationToken);

        return new SalesDashboardDto(version.Id, version.VersionNumber, version.Name, currency,
            bQty, fQty, bFree, fFree, bGross, fGross, bDiscount, fDiscount, bReturn, fReturn,
            bNet, fNet, bCogs, fCogs, bCompanyDiscount, fCompanyDiscount, bProfit, fProfit, fNet - bNet,
            monthly, dimensions, selected?.Id, drilldown);
    }

    private async Task<IReadOnlyList<SalesDashboardDrilldownRowDto>> BuildDrilldownAsync(
        Guid versionId,
        Guid companyId,
        Guid dimensionId,
        IReadOnlyDictionary<string, MeasureRef> measures,
        string currency,
        decimal totalBQ,
        decimal totalFQ,
        decimal totalBGross,
        decimal totalFGross,
        decimal totalBNet,
        decimal totalFNet,
        decimal totalBCogs,
        decimal totalFCogs,
        decimal totalBCompanyDiscount,
        decimal totalFCompanyDiscount,
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
                && (x.BudgetFact.ValueKind == ValueKind.Budget || x.BudgetFact.ValueKind == ValueKind.Forecast)
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
            var bNet = S("NET_SALES", ValueKind.Budget);
            var fNet = S("NET_SALES", ValueKind.Forecast);
            var bCogs = S("SALES_COGS_TOTAL", ValueKind.Budget);
            var fCogs = S("SALES_COGS_TOTAL", ValueKind.Forecast);
            var bCd = S("PURCHASE_COMPANY_DISCOUNT", ValueKind.Budget);
            var fCd = S("PURCHASE_COMPANY_DISCOUNT", ValueKind.Forecast);
            return new SalesDashboardDrilldownRowDto(member.Id, member.Code, member.Name,
                S("SALES_QTY", ValueKind.Budget), S("SALES_QTY", ValueKind.Forecast),
                S("GROSS_SALES", ValueKind.Budget), S("GROSS_SALES", ValueKind.Forecast),
                bNet, fNet, bCogs, fCogs, bNet - bCogs + bCd, fNet - fCogs + fCd, fNet - bNet);
        }).ToList();

        decimal Alloc(Func<SalesDashboardDrilldownRowDto, decimal> selector) => rows.Sum(selector);
        var ubq = totalBQ - Alloc(x => x.BudgetQuantity);
        var ufq = totalFQ - Alloc(x => x.ForecastQuantity);
        var ubg = totalBGross - Alloc(x => x.BudgetGrossSales);
        var ufg = totalFGross - Alloc(x => x.ForecastGrossSales);
        var ubn = totalBNet - Alloc(x => x.BudgetNetSales);
        var ufn = totalFNet - Alloc(x => x.ForecastNetSales);
        var ubc = totalBCogs - Alloc(x => x.BudgetCogs);
        var ufc = totalFCogs - Alloc(x => x.ForecastCogs);
        var allocatedBCd = links.Where(x => x.MeasureId == ids["PURCHASE_COMPANY_DISCOUNT"] && x.ValueKind == ValueKind.Budget).Sum(x => x.Value);
        var allocatedFCd = links.Where(x => x.MeasureId == ids["PURCHASE_COMPANY_DISCOUNT"] && x.ValueKind == ValueKind.Forecast).Sum(x => x.Value);
        var ubp = ubn - ubc + (totalBCompanyDiscount - allocatedBCd);
        var ufp = ufn - ufc + (totalFCompanyDiscount - allocatedFCd);
        if (ubq != 0 || ufq != 0 || ubg != 0 || ufg != 0 || ubn != 0 || ufn != 0 || ubc != 0 || ufc != 0 || ubp != 0 || ufp != 0)
            rows.Add(new SalesDashboardDrilldownRowDto(Guid.Empty, "UNALLOCATED", "بدون تفکیک", ubq, ufq, ubg, ufg, ubn, ufn, ubc, ufc, ubp, ufp, ufn - ubn));

        return rows.OrderByDescending(x => x.ForecastNetSales).ThenByDescending(x => x.BudgetNetSales).Take(take).ToList();
    }

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct))
            throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private async Task<string> GetBaseCurrencyAsync(Guid tenantId, CancellationToken ct) =>
        await db.Currencies.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync(ct) ?? "IRR";

    private sealed record MeasureRef(Guid Id, string Code, MeasureValueType ValueType);
    private sealed record SalesFact(Guid Id, Guid PeriodId, Guid MeasureId, ValueKind ValueKind, decimal Value);
}
