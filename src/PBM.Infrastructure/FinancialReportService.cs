using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class FinancialReportService(PbmDbContext db, IUserContext user) : IFinancialReportService
{
    private sealed record PeriodRef(Guid Id, string Name, int Sequence);

    private static readonly IReadOnlyList<(string Code, string Name)> ProfitLossRows = new[]
    {
        ("GROSS_SALES", "فروش ناخالص کالای تجاری"),
        ("SALES_DISCOUNT", "تخفیفات فروش"),
        ("NET_SALES", "فروش خالص"),
        ("COGS", "قیمت تمام شده کالای تجاری فروش رفته"),
        ("GROSS_PROFIT", "سود (زیان) ناخالص"),
        ("ADMIN_EXPENSE", "هزینه‌های اداری، عمومی و فروش"),
        ("OPERATING_PROFIT", "سود (زیان) عملیاتی"),
        ("FINANCE_COST", "هزینه‌های مالی"),
        ("PROFIT_BEFORE_TAX", "سود (زیان) قبل از مالیات"),
        ("TAX", "مالیات"),
        ("NET_PROFIT", "سود خالص پس از کسر مالیات")
    };

    private static readonly IReadOnlyList<(string Code, string Name)> BalanceSheetRows = new[]
    {
        ("CASH_BANK", "موجودی نقد و بانک"),
        ("TRADE_RECEIVABLE", "حساب‌ها و اسناد دریافتنی تجاری"),
        ("INVENTORY", "موجودی مواد و کالا"),
        ("CURRENT_ASSETS", "جمع دارایی‌های جاری"),
        ("TOTAL_ASSETS", "جمع دارایی‌ها"),
        ("TRADE_PAYABLE", "حساب‌ها و اسناد پرداختنی تجاری"),
        ("CURRENT_LIABILITIES", "جمع بدهی‌های جاری"),
        ("EQUITY", "جمع حقوق صاحبان سهام"),
        ("TOTAL_LIAB_EQUITY", "جمع بدهی‌ها و حقوق صاحبان سهام")
    };

    private static readonly IReadOnlyList<(string Code, string Name)> CashFlowRows = new[]
    {
        ("CFO", "جریان خالص نقد حاصل از فعالیت‌های عملیاتی"),
        ("CFI", "جریان خالص نقد حاصل از فعالیت‌های سرمایه‌گذاری"),
        ("CFF", "جریان خالص نقد حاصل از فعالیت‌های تامین مالی"),
        ("ENDING_CASH", "مانده موجودی نقد در پایان دوره")
    };

    public async Task<FinancialReportDto> GetAsync(Guid companyId, Guid fiscalYearId, FinancialReportType type, ValueKind valueKind = ValueKind.Budget, Guid? versionId = null, CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, cancellationToken);
        if (!await db.FiscalYears.AnyAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");

        var version = versionId.HasValue
            ? await db.BudgetVersions.AsNoTracking().Where(x => x.Id == versionId.Value && x.BudgetPlan!.CompanyId == companyId && x.BudgetPlan.FiscalYearId == fiscalYearId && x.BudgetPlan.BudgetModel!.Code == "FINSTAT")
                .Select(x => new { x.Id, x.Name, x.VersionNumber }).SingleOrDefaultAsync(cancellationToken)
            : await db.BudgetVersions.AsNoTracking().Where(x => x.BudgetPlan!.CompanyId == companyId && x.BudgetPlan.FiscalYearId == fiscalYearId && x.BudgetPlan.BudgetModel!.Code == "FINSTAT")
                .OrderByDescending(x => x.VersionNumber).ThenByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, x.Name, x.VersionNumber }).FirstOrDefaultAsync(cancellationToken);

        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYearId).OrderBy(x => x.Sequence)
            .Select(x => new PeriodRef(x.Id, x.Name, x.Sequence)).ToListAsync(cancellationToken);

        var template = type switch
        {
            FinancialReportType.BalanceSheet => BalanceSheetRows,
            FinancialReportType.CashFlow => CashFlowRows,
            _ => ProfitLossRows
        };

        if (version is null)
            return Empty(type, companyId, fiscalYearId, valueKind, periods, template);

        var accountDimensionId = await db.Dimensions.Where(x => x.TenantId == user.TenantId && x.Code == "ACCOUNT").Select(x => x.Id).SingleAsync(cancellationToken);
        var statementMeasureId = await db.Measures.Where(x => x.BudgetModel!.TenantId == user.TenantId && x.BudgetModel.Code == "FINSTAT" && x.Code == "STATEMENT_AMOUNT")
            .Select(x => x.Id).SingleAsync(cancellationToken);

        var codes = template.Select(t => t.Code).ToArray();
        var accountMembers = await db.DimensionMembers.AsNoTracking().Where(x => x.DimensionId == accountDimensionId && codes.Contains(x.Code))
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(cancellationToken);
        var byMember = accountMembers.ToDictionary(x => x.Id);

        var facts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == version.Id && x.MeasureId == statementMeasureId && x.ValueKind == valueKind)
            .Select(x => new
            {
                x.PeriodId,
                x.Value,
                AccountMemberId = x.Dimensions.Where(d => d.DimensionId == accountDimensionId).Select(d => (Guid?)d.MemberId).FirstOrDefault()
            })
            .Where(x => x.AccountMemberId.HasValue)
            .ToListAsync(cancellationToken);

        var values = facts.Where(x => byMember.ContainsKey(x.AccountMemberId!.Value))
            .GroupBy(x => (x.AccountMemberId!.Value, x.PeriodId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Value));

        var memberByCode = accountMembers.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var rows = new List<FinancialReportRowDto>(template.Count);
        for (var index = 0; index < template.Count; index++)
        {
            var item = template[index];
            memberByCode.TryGetValue(item.Code, out var member);
            var cells = periods.Select(p => new FinancialReportCellDto(p.Id, p.Name, p.Sequence,
                member is not null && values.TryGetValue((member.Id, p.Id), out var value) ? value : 0m)).ToList();
            var total = type == FinancialReportType.BalanceSheet ? cells.OrderBy(x => x.Sequence).LastOrDefault()?.Value ?? 0m : cells.Sum(x => x.Value);
            rows.Add(new FinancialReportRowDto(item.Code, member?.Name ?? item.Name, index + 1, cells, total));
        }

        return new FinancialReportDto(type, companyId, fiscalYearId, version.Id, $"{version.Name} (V{version.VersionNumber})", valueKind, rows);
    }

    private static FinancialReportDto Empty(FinancialReportType type, Guid companyId, Guid fiscalYearId, ValueKind valueKind, IReadOnlyList<PeriodRef> periods, IReadOnlyList<(string Code, string Name)> template)
    {
        var rows = template.Select((item, index) => new FinancialReportRowDto(item.Code, item.Name, index + 1,
            periods.Select(p => new FinancialReportCellDto(p.Id, p.Name, p.Sequence, 0m)).ToList(), 0m)).ToList();
        return new FinancialReportDto(type, companyId, fiscalYearId, null, null, valueKind, rows);
    }

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId, cancellationToken))
            throw new UnauthorizedAccessException("Company is outside the current tenant.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }
}
