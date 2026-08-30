using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class FinancialReportService(
    PbmDbContext db,
    IUserContext user,
    CommercialPlanningProvisioner provisioner) : IFinancialReportService
{
    private sealed record PeriodRef(Guid Id, string Name, int Sequence);
    private sealed record VersionRef(Guid Id, string Name, int VersionNumber, Guid ModelId);

    private static readonly IReadOnlyList<(string Code, string Name)> ProfitLossRows = new[]
    {
        ("GROSS_SALES", "فروش ناخالص کالای تجاری"),
        ("SALES_DISCOUNT", "تخفیفات فروش"),
        ("SALES_RETURN", "برگشت از فروش"),
        ("NET_SALES", "فروش خالص"),
        ("COGS", "قیمت تمام‌شده کالای تجاری فروش‌رفته"),
        ("PURCHASE_COMPANY_DISCOUNT", "تخفیفات کمپانی (خرید)"),
        ("TOTAL_COGS", "جمع قیمت تمام‌شده"),
        ("GROSS_PROFIT", "سود (زیان) ناخالص"),
        ("ADMIN_EXPENSE", "سایر هزینه‌های اداری و عمومی"),
        ("OTHER_OPERATING_NET", "خالص سایر درآمدها (هزینه‌های) عملیاتی"),
        ("OPERATING_PROFIT", "سود (زیان) عملیاتی"),
        ("FINANCE_COST", "هزینه‌های مالی"),
        ("OTHER_NON_OPERATING_NET", "خالص سایر درآمدها (هزینه‌های) غیرعملیاتی"),
        ("PROFIT_BEFORE_TAX", "سود (زیان) ویژه قبل از مالیات"),
        ("TAX", "مالیات"),
        ("NET_PROFIT", "سود خالص پس از کسر مالیات"),
        ("RESERVE_TRANSFER", "انتقال از ذخایر و اندوخته‌ها"),
        ("PRIOR_RETAINED_EARNINGS", "سود انباشته نقل از سال قبل"),
        ("PRIOR_ADJUSTMENTS", "خالص تعدیلات طی سال"),
        ("ALLOCATABLE_PROFIT", "سود قابل تخصیص"),
        ("LEGAL_RESERVE", "اندوخته قانونی"),
        ("APPROPRIATION_ADJUSTMENTS", "تعدیلات"),
        ("DIVIDEND", "سهم سود"),
        ("CLOSING_RETAINED_EARNINGS", "مانده سود (زیان) نقل به سال بعد"),
        ("CASH_SALES_DISCOUNT", "تخفیفات ریالی فروش"),
        ("FREE_SALES_DISCOUNT", "تخفیفات جنسی فروش")
    };

    private static readonly IReadOnlyList<(string Code, string Name)> BalanceSheetRows = new[]
    {
        ("CASH_BANK", "موجودی نقد و بانک"), ("TRADE_RECEIVABLE", "حساب‌ها و اسناد دریافتنی تجاری"),
        ("INVENTORY", "موجودی مواد و کالا"), ("CURRENT_ASSETS", "جمع دارایی‌های جاری"), ("TOTAL_ASSETS", "جمع دارایی‌ها"),
        ("TRADE_PAYABLE", "حساب‌ها و اسناد پرداختنی تجاری"), ("CURRENT_LIABILITIES", "جمع بدهی‌های جاری"),
        ("EQUITY", "جمع حقوق صاحبان سهام"), ("TOTAL_LIAB_EQUITY", "جمع بدهی‌ها و حقوق صاحبان سهام")
    };

    private static readonly IReadOnlyList<(string Code, string Name)> CashFlowRows = new[]
    {
        ("CFO", "جریان خالص نقد حاصل از فعالیت‌های عملیاتی"), ("CFI", "جریان خالص نقد حاصل از فعالیت‌های سرمایه‌گذاری"),
        ("CFF", "جریان خالص نقد حاصل از فعالیت‌های تامین مالی"), ("ENDING_CASH", "مانده موجودی نقد در پایان دوره")
    };

    public async Task<FinancialReportDto> GetAsync(
        Guid companyId,
        Guid fiscalYearId,
        FinancialReportType type,
        ValueKind valueKind = ValueKind.Budget,
        Guid? versionId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, cancellationToken);
        if (!await db.FiscalYears.AnyAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");
        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYearId).OrderBy(x => x.Sequence)
            .Select(x => new PeriodRef(x.Id, x.Name, x.Sequence)).ToListAsync(cancellationToken);

        if (type == FinancialReportType.ProfitLoss)
            return await BuildOperationalProfitLossAsync(companyId, fiscalYearId, valueKind, periods, cancellationToken);

        var template = type == FinancialReportType.BalanceSheet ? BalanceSheetRows : CashFlowRows;
        return await BuildFinStatAsync(companyId, fiscalYearId, type, valueKind, versionId, periods, template, cancellationToken);
    }

    private async Task<FinancialReportDto> BuildOperationalProfitLossAsync(
        Guid companyId,
        Guid fiscalYearId,
        ValueKind valueKind,
        IReadOnlyList<PeriodRef> periods,
        CancellationToken ct)
    {
        var tenantId = await db.Companies.AsNoTracking().Where(x => x.Id == companyId).Select(x => x.TenantId).SingleAsync(ct);
        await provisioner.EnsureSalesAsync(tenantId, ct);
        await provisioner.EnsureExpenseAsync(tenantId, ct);
        var currency = await db.Currencies.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync(ct) ?? "IRR";
        var tradeVersion = await LatestVersionAsync(companyId, fiscalYearId, "TRADE", ct);
        var expenseVersion = await LatestVersionAsync(companyId, fiscalYearId, "EXPENSE", ct);

        var values = new Dictionary<(string Code, Guid PeriodId), decimal>();
        decimal Get(string code, Guid periodId) => values.GetValueOrDefault((code, periodId));
        void Set(string code, Guid periodId, decimal amount) => values[(code, periodId)] = amount;

        if (tradeVersion is not null)
        {
            var tradeCodes = new[]
            {
                "GROSS_SALES", "SALES_DISCOUNT", "FOC_SALES_AMOUNT", "SALES_RETURN",
                "COGS_AMOUNT", "FOC_COST", "PURCHASE_COMPANY_DISCOUNT"
            };
            var measures = await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == tradeVersion.ModelId && tradeCodes.Contains(x.Code))
                .Select(x => new { x.Id, x.Code }).ToListAsync(ct);
            var byId = measures.ToDictionary(x => x.Id, x => x.Code);
            var measureIds = byId.Keys.ToArray();
            var tradeFacts = await db.BudgetFacts.AsNoTracking().Where(x => x.VersionId == tradeVersion.Id && x.ValueKind == valueKind && measureIds.Contains(x.MeasureId) && x.CurrencyCode == currency)
                .Select(x => new { x.PeriodId, x.MeasureId, x.Value }).ToListAsync(ct);
            foreach (var group in tradeFacts.GroupBy(x => (Code: byId[x.MeasureId], x.PeriodId)))
                Set(group.Key.Code, group.Key.PeriodId, group.Sum(x => x.Value));
        }

        if (expenseVersion is not null)
        {
            var measureId = await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == expenseVersion.ModelId && x.Code == "EXPENSE_AMOUNT").Select(x => x.Id).SingleAsync(ct);
            var classDimensionId = await db.Dimensions.AsNoTracking().Where(x => x.TenantId == tenantId && x.Code == "EXPENSECLASS").Select(x => x.Id).SingleAsync(ct);
            var classes = await db.DimensionMembers.AsNoTracking().Where(x => x.DimensionId == classDimensionId && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
                .Select(x => new { x.Id, x.Code }).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
            var expenseFacts = await db.BudgetFacts.AsNoTracking().Include(x => x.Dimensions)
                .Where(x => x.VersionId == expenseVersion.Id && x.ValueKind == valueKind && x.MeasureId == measureId && x.CurrencyCode == currency)
                .ToListAsync(ct);
            foreach (var periodGroup in expenseFacts.GroupBy(x => x.PeriodId))
            {
                decimal ClassSum(params string[] codes) => periodGroup.Where(f =>
                {
                    var id = f.Dimensions.Where(d => d.DimensionId == classDimensionId).Select(d => (Guid?)d.MemberId).SingleOrDefault();
                    return id.HasValue && classes.TryGetValue(id.Value, out var code) && codes.Contains(code, StringComparer.OrdinalIgnoreCase);
                }).Sum(x => x.Value);
                var classified = periodGroup.Where(f => f.Dimensions.Any(d => d.DimensionId == classDimensionId && classes.ContainsKey(d.MemberId))).ToList();
                var unclassified = periodGroup.Sum(x => x.Value) - classified.Sum(x => x.Value);
                Set("ADMIN_EXPENSE", periodGroup.Key,
                    ClassSum("PERSONNEL", "ADMIN_GENERAL", "MARKETING", "SELLING") + unclassified);
                Set("OTHER_OPERATING_NET", periodGroup.Key,
                    ClassSum("OTHER_OPERATING_INCOME") - ClassSum("OTHER_OPERATING_EXPENSE"));
                Set("FINANCE_COST", periodGroup.Key, ClassSum("FINANCIAL_EXPENSE"));
                Set("OTHER_NON_OPERATING_NET", periodGroup.Key,
                    ClassSum("OTHER_NON_OPERATING_INCOME") - ClassSum("OTHER_NON_OPERATING_EXPENSE"));
                Set("TAX", periodGroup.Key, ClassSum("TAX"));
            }
        }

        var finStatSupplement = await GetFinStatSupplementAsync(companyId, fiscalYearId, valueKind, periods, ct);
        foreach (var pair in finStatSupplement) values.TryAdd(pair.Key, pair.Value);

        foreach (var period in periods)
        {
            var gross = Get("GROSS_SALES", period.Id);
            var cashDiscount = Get("SALES_DISCOUNT", period.Id);
            var freeDiscount = Get("FOC_SALES_AMOUNT", period.Id);
            var discount = cashDiscount + freeDiscount;
            var salesReturn = Get("SALES_RETURN", period.Id);
            var netSales = gross - discount - salesReturn;
            var cogs = Get("COGS_AMOUNT", period.Id) + Get("FOC_COST", period.Id);
            var companyDiscount = Get("PURCHASE_COMPANY_DISCOUNT", period.Id);
            var totalCogs = cogs - companyDiscount;
            var grossProfit = netSales - totalCogs;
            var admin = Get("ADMIN_EXPENSE", period.Id);
            var otherOperating = Get("OTHER_OPERATING_NET", period.Id);
            var operatingProfit = grossProfit - admin + otherOperating;
            var finance = Get("FINANCE_COST", period.Id);
            var nonOperating = Get("OTHER_NON_OPERATING_NET", period.Id);
            var preTax = operatingProfit - finance + nonOperating;
            var tax = Get("TAX", period.Id);
            var netProfit = preTax - tax;

            Set("SALES_DISCOUNT", period.Id, discount);
            Set("NET_SALES", period.Id, netSales);
            Set("COGS", period.Id, cogs);
            Set("TOTAL_COGS", period.Id, totalCogs);
            Set("GROSS_PROFIT", period.Id, grossProfit);
            Set("OPERATING_PROFIT", period.Id, operatingProfit);
            Set("PROFIT_BEFORE_TAX", period.Id, preTax);
            Set("NET_PROFIT", period.Id, netProfit);
            Set("CASH_SALES_DISCOUNT", period.Id, cashDiscount);
            Set("FREE_SALES_DISCOUNT", period.Id, freeDiscount);

            var allocatable = netProfit + Get("RESERVE_TRANSFER", period.Id) + Get("PRIOR_RETAINED_EARNINGS", period.Id) + Get("PRIOR_ADJUSTMENTS", period.Id);
            Set("ALLOCATABLE_PROFIT", period.Id, allocatable);
            Set("CLOSING_RETAINED_EARNINGS", period.Id,
                allocatable - Get("LEGAL_RESERVE", period.Id) - Get("APPROPRIATION_ADJUSTMENTS", period.Id) - Get("DIVIDEND", period.Id));
        }

        var rows = ProfitLossRows.Select((item, index) =>
        {
            var cells = periods.Select(p => new FinancialReportCellDto(p.Id, p.Name, p.Sequence, Get(item.Code, p.Id))).ToList();
            return new FinancialReportRowDto(item.Code, item.Name, index + 1, cells, cells.Sum(x => x.Value));
        }).ToList();
        var versionName = string.Join(" + ", new[]
        {
            tradeVersion is null ? null : $"TRADE {tradeVersion.Name} (V{tradeVersion.VersionNumber})",
            expenseVersion is null ? null : $"EXPENSE {expenseVersion.Name} (V{expenseVersion.VersionNumber})"
        }.Where(x => x is not null));
        return new FinancialReportDto(FinancialReportType.ProfitLoss, companyId, fiscalYearId, null,
            string.IsNullOrWhiteSpace(versionName) ? "گزارش عملیاتی تجمیعی" : versionName, valueKind, rows);
    }

    private async Task<Dictionary<(string Code, Guid PeriodId), decimal>> GetFinStatSupplementAsync(
        Guid companyId,
        Guid fiscalYearId,
        ValueKind valueKind,
        IReadOnlyList<PeriodRef> periods,
        CancellationToken ct)
    {
        var result = new Dictionary<(string Code, Guid PeriodId), decimal>();
        var version = await LatestVersionAsync(companyId, fiscalYearId, "FINSTAT", ct);
        if (version is null) return result;
        var accountDimensionId = await db.Dimensions.AsNoTracking().Where(x => x.TenantId == user.TenantId && x.Code == "ACCOUNT").Select(x => x.Id).SingleAsync(ct);
        var measureId = await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == version.ModelId && x.Code == "STATEMENT_AMOUNT").Select(x => x.Id).SingleAsync(ct);
        var codes = new[] { "RESERVE_TRANSFER", "PRIOR_RETAINED_EARNINGS", "PRIOR_ADJUSTMENTS", "LEGAL_RESERVE", "APPROPRIATION_ADJUSTMENTS", "DIVIDEND" };
        var members = await db.DimensionMembers.AsNoTracking().Where(x => x.DimensionId == accountDimensionId && (x.CompanyId == null || x.CompanyId == companyId) && codes.Contains(x.Code))
            .Select(x => new { x.Id, x.Code }).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        if (members.Count == 0) return result;
        var facts = await db.BudgetFacts.AsNoTracking().Where(x => x.VersionId == version.Id && x.MeasureId == measureId && x.ValueKind == valueKind)
            .Select(x => new { x.PeriodId, x.Value, MemberId = x.Dimensions.Where(d => d.DimensionId == accountDimensionId).Select(d => (Guid?)d.MemberId).FirstOrDefault() })
            .Where(x => x.MemberId.HasValue).ToListAsync(ct);
        foreach (var group in facts.Where(x => members.ContainsKey(x.MemberId!.Value)).GroupBy(x => (Code: members[x.MemberId!.Value], x.PeriodId)))
            result[group.Key] = group.Sum(x => x.Value);
        return result;
    }

    private async Task<FinancialReportDto> BuildFinStatAsync(
        Guid companyId,
        Guid fiscalYearId,
        FinancialReportType type,
        ValueKind valueKind,
        Guid? versionId,
        IReadOnlyList<PeriodRef> periods,
        IReadOnlyList<(string Code, string Name)> template,
        CancellationToken ct)
    {
        VersionRef? version;
        if (versionId.HasValue)
        {
            version = await db.BudgetVersions.AsNoTracking().Where(x => x.Id == versionId.Value && x.BudgetPlan!.CompanyId == companyId && x.BudgetPlan.FiscalYearId == fiscalYearId && x.BudgetPlan.BudgetModel!.Code == "FINSTAT")
                .Select(x => new VersionRef(x.Id, x.Name, x.VersionNumber, x.BudgetPlan!.BudgetModelId)).SingleOrDefaultAsync(ct);
        }
        else version = await LatestVersionAsync(companyId, fiscalYearId, "FINSTAT", ct);
        if (version is null) return Empty(type, companyId, fiscalYearId, valueKind, periods, template);

        var accountDimensionId = await db.Dimensions.Where(x => x.TenantId == user.TenantId && x.Code == "ACCOUNT").Select(x => x.Id).SingleAsync(ct);
        var statementMeasureId = await db.Measures.Where(x => x.BudgetModelId == version.ModelId && x.Code == "STATEMENT_AMOUNT").Select(x => x.Id).SingleAsync(ct);
        var codes = template.Select(t => t.Code).ToArray();
        var candidates = await db.DimensionMembers.AsNoTracking().Where(x => x.DimensionId == accountDimensionId && (x.CompanyId == null || x.CompanyId == companyId) && codes.Contains(x.Code))
            .Select(x => new { x.Id, x.Code, x.Name, x.CompanyId }).ToListAsync(ct);
        var members = candidates.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(x => x.CompanyId == companyId).First()).ToList();
        var byId = members.ToDictionary(x => x.Id);
        var facts = await db.BudgetFacts.AsNoTracking().Where(x => x.VersionId == version.Id && x.MeasureId == statementMeasureId && x.ValueKind == valueKind)
            .Select(x => new { x.PeriodId, x.Value, MemberId = x.Dimensions.Where(d => d.DimensionId == accountDimensionId).Select(d => (Guid?)d.MemberId).FirstOrDefault() }).Where(x => x.MemberId.HasValue).ToListAsync(ct);
        var factValues = facts.Where(x => byId.ContainsKey(x.MemberId!.Value)).GroupBy(x => (x.MemberId!.Value, x.PeriodId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Value));
        var memberByCode = members.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var rows = template.Select((item, index) =>
        {
            memberByCode.TryGetValue(item.Code, out var member);
            var cells = periods.Select(p => new FinancialReportCellDto(p.Id, p.Name, p.Sequence, member is not null && factValues.TryGetValue((member.Id, p.Id), out var value) ? value : 0m)).ToList();
            var total = type == FinancialReportType.BalanceSheet ? cells.LastOrDefault()?.Value ?? 0m : cells.Sum(x => x.Value);
            return new FinancialReportRowDto(item.Code, member?.Name ?? item.Name, index + 1, cells, total);
        }).ToList();
        return new FinancialReportDto(type, companyId, fiscalYearId, version.Id, $"{version.Name} (V{version.VersionNumber})", valueKind, rows);
    }

    private async Task<VersionRef?> LatestVersionAsync(Guid companyId, Guid fiscalYearId, string modelCode, CancellationToken ct) =>
        await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.CompanyId == companyId && x.BudgetPlan.FiscalYearId == fiscalYearId && x.BudgetPlan.BudgetModel!.Code == modelCode && x.Status != BudgetStatus.Rejected)
            .OrderByDescending(x => x.VersionNumber).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new VersionRef(x.Id, x.Name, x.VersionNumber, x.BudgetPlan!.BudgetModelId)).FirstOrDefaultAsync(ct);

    private static FinancialReportDto Empty(FinancialReportType type, Guid companyId, Guid fiscalYearId, ValueKind valueKind, IReadOnlyList<PeriodRef> periods, IReadOnlyList<(string Code, string Name)> template) =>
        new(type, companyId, fiscalYearId, null, null, valueKind,
            template.Select((item, index) => new FinancialReportRowDto(item.Code, item.Name, index + 1, periods.Select(p => new FinancialReportCellDto(p.Id, p.Name, p.Sequence, 0m)).ToList(), 0m)).ToList());

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId, ct)) throw new UnauthorizedAccessException("Company is outside the current tenant.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
    }
}
