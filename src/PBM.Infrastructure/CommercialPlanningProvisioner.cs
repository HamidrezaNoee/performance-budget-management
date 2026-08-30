using Microsoft.EntityFrameworkCore;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class CommercialPlanningProvisioner(PbmDbContext db)
{
    public async Task EnsureSalesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var trade = await db.BudgetModels.Include(x => x.Dimensions).Include(x => x.Measures)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "TRADE" && x.IsActive, ct)
            ?? throw new InvalidOperationException("TRADE budget model is not available.");

        var requestedCodes = new[]
        {
            "PRODUCT", "SUPPLIER", "BRAND", "CUSTOMER", "REGION", "DEPARTMENT", "COSTCENTER",
            "CONTRACT", "CURRENCY", "ACCOUNT", "PROGRAM", "ACTIVITY", "PROJECT", "FUNDINGSOURCE"
        };
        var dimensions = await db.Dimensions.Where(x => x.TenantId == tenantId && requestedCodes.Contains(x.Code) && x.IsActive)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        var attached = trade.Dimensions.Select(x => x.DimensionId).ToHashSet();
        var sequence = trade.Dimensions.Count == 0 ? 1 : trade.Dimensions.Max(x => x.Sequence) + 1;
        foreach (var code in requestedCodes)
        {
            if (!dimensions.TryGetValue(code, out var dimension) || attached.Contains(dimension.Id)) continue;
            var link = new BudgetModelDimension
            {
                BudgetModelId = trade.Id,
                DimensionId = dimension.Id,
                Sequence = sequence++,
                IsRequired = code == "PRODUCT"
            };
            db.BudgetModelDimensions.Add(link);
            trade.Dimensions.Add(link);
            attached.Add(dimension.Id);
        }

        var order = trade.Measures.Count == 0 ? 1 : trade.Measures.Max(x => x.DisplayOrder) + 1;
        EnsureMeasure(trade, "SALES_QTY", "تعداد فروش", "واحد", MeasureValueType.Quantity, ref order);
        EnsureMeasure(trade, "FREE_SALES_QTY", "فروش رایگان / آفر", "واحد", MeasureValueType.Quantity, ref order);
        EnsureMeasure(trade, "SALES_PRICE", "نرخ فروش", "ریال", MeasureValueType.Rate, ref order, MeasureAggregation.Average);
        EnsureMeasure(trade, "GROSS_SALES", "فروش ناخالص کالای تجاری", "ریال", MeasureValueType.Amount, ref order,
            formula: "[SALES_QTY] * [SALES_PRICE]");
        EnsureMeasure(trade, "SALES_DISCOUNT", "تخفیفات ریالی فروش", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "SALES_RETURN", "برگشت از فروش", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "FOC_SALES_AMOUNT", "تخفیف / جایزه جنسی فروش", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "NET_SALES", "فروش خالص", "ریال", MeasureValueType.Amount, ref order,
            formula: "[GROSS_SALES] - [SALES_DISCOUNT] - [SALES_RETURN]");
        EnsureMeasure(trade, "COGS_AMOUNT", "قیمت تمام‌شده کالای تجاری فروش‌رفته", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "PURCHASE_COMPANY_DISCOUNT", "تخفیفات کمپانی (خرید)", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "SALES_GROSS_MARGIN", "سود ناخالص فروش", "ریال", MeasureValueType.Amount, ref order,
            formula: "[NET_SALES] - [COGS_AMOUNT] + [PURCHASE_COMPANY_DISCOUNT]");

        var netSales = trade.Measures.FirstOrDefault(x => x.Code == "NET_SALES");
        if (netSales is not null)
        {
            netSales.IsCalculated = true;
            netSales.FormulaExpression = "[GROSS_SALES] - [SALES_DISCOUNT] - [SALES_RETURN]";
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureExpenseAsync(Guid tenantId, CancellationToken ct = default)
    {
        var expense = await db.BudgetModels.Include(x => x.Dimensions).Include(x => x.Measures)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "EXPENSE" && x.IsActive, ct)
            ?? throw new InvalidOperationException("EXPENSE budget model is not available.");

        var dimensions = await db.Dimensions.Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        var expenseClass = await EnsureDimensionAsync(dimensions, tenantId, "EXPENSECLASS", "طبقه هزینه / درآمد", false, ct);
        var expenseItem = await EnsureDimensionAsync(dimensions, tenantId, "EXPENSEITEM", "ردیف هزینه / درآمد", false, ct);

        foreach (var (code, name) in ExpenseClasses)
            await EnsureMemberAsync(expenseClass, code, name, ct);
        foreach (var (code, name) in ExpenseItems)
            await EnsureMemberAsync(expenseItem, code, name, ct);

        if (dimensions.TryGetValue("ACCOUNT", out var account))
            await EnsureMemberAsync(account, "EXPENSE_BUDGET", "حساب بودجه هزینه و درآمد", ct);

        var requestedCodes = new[]
        {
            "DEPARTMENT", "ACCOUNT", "COSTCENTER", "EXPENSECLASS", "EXPENSEITEM",
            "PROGRAM", "ACTIVITY", "PROJECT", "FUNDINGSOURCE", "CONTRACT", "REGION"
        };
        var attached = expense.Dimensions.Select(x => x.DimensionId).ToHashSet();
        var sequence = expense.Dimensions.Count == 0 ? 1 : expense.Dimensions.Max(x => x.Sequence) + 1;
        foreach (var code in requestedCodes)
        {
            if (!dimensions.TryGetValue(code, out var dimension) || attached.Contains(dimension.Id)) continue;
            var link = new BudgetModelDimension
            {
                BudgetModelId = expense.Id,
                DimensionId = dimension.Id,
                Sequence = sequence++,
                IsRequired = false
            };
            db.BudgetModelDimensions.Add(link);
            expense.Dimensions.Add(link);
            attached.Add(dimension.Id);
        }

        var order = expense.Measures.Count == 0 ? 1 : expense.Measures.Max(x => x.DisplayOrder) + 1;
        EnsureMeasure(expense, "EXPENSE_AMOUNT", "مبلغ هزینه / درآمد", "ریال", MeasureValueType.Amount, ref order);
        await db.SaveChangesAsync(ct);
    }

    private async Task<DimensionDefinition> EnsureDimensionAsync(
        IDictionary<string, DimensionDefinition> dimensions,
        Guid tenantId,
        string code,
        string name,
        bool hierarchical,
        CancellationToken ct)
    {
        if (dimensions.TryGetValue(code, out var existing)) return existing;
        var dimension = new DimensionDefinition
        {
            TenantId = tenantId,
            Code = code,
            Name = name,
            IsSystem = true,
            IsHierarchical = hierarchical
        };
        db.Dimensions.Add(dimension);
        dimensions[code] = dimension;
        await db.SaveChangesAsync(ct);
        return dimension;
    }

    private async Task EnsureMemberAsync(DimensionDefinition dimension, string code, string name, CancellationToken ct)
    {
        if (await db.DimensionMembers.AnyAsync(x => x.DimensionId == dimension.Id && x.Code == code, ct)) return;
        db.DimensionMembers.Add(new DimensionMember
        {
            DimensionId = dimension.Id,
            CompanyId = null,
            Code = code,
            Name = name
        });
        await db.SaveChangesAsync(ct);
    }

    private static void EnsureMeasure(
        BudgetModel model,
        string code,
        string name,
        string unit,
        MeasureValueType type,
        ref int order,
        MeasureAggregation aggregation = MeasureAggregation.Sum,
        string? formula = null)
    {
        var existing = model.Measures.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (formula is not null)
            {
                existing.IsCalculated = true;
                existing.FormulaExpression = formula;
            }
            return;
        }
        model.Measures.Add(new MeasureDefinition
        {
            BudgetModelId = model.Id,
            Code = code,
            Name = name,
            Unit = unit,
            ValueType = type,
            Aggregation = aggregation,
            DisplayOrder = order++,
            IsCalculated = formula is not null,
            FormulaExpression = formula
        });
    }

    private static readonly (string Code, string Name)[] ExpenseClasses =
    [
        ("PERSONNEL", "حقوق و دستمزد و هزینه‌های پرسنلی"),
        ("ADMIN_GENERAL", "سایر هزینه‌های اداری و عمومی"),
        ("MARKETING", "هزینه‌های بازاریابی"),
        ("SELLING", "هزینه‌های فروش و توزیع"),
        ("OTHER_OPERATING_INCOME", "سایر درآمدهای عملیاتی"),
        ("OTHER_OPERATING_EXPENSE", "سایر هزینه‌های عملیاتی"),
        ("FINANCIAL_EXPENSE", "هزینه‌های مالی"),
        ("OTHER_NON_OPERATING_INCOME", "سایر درآمدهای غیرعملیاتی"),
        ("OTHER_NON_OPERATING_EXPENSE", "سایر هزینه‌های غیرعملیاتی"),
        ("TAX", "مالیات")
    ];

    private static readonly (string Code, string Name)[] ExpenseItems =
    [
        ("SALARY_BASE", "حقوق پایه"), ("FOOD_ALLOWANCE", "خواروبار"), ("HOUSING_ALLOWANCE", "حق مسکن"),
        ("CHILD_ALLOWANCE", "حق اولاد"), ("OVERTIME", "اضافه‌کاری"), ("MISSION", "ماموریت"),
        ("COMMUTE", "ایاب و ذهاب"), ("PHONE_ALLOWANCE", "کمک هزینه تلفن"), ("SENIORITY", "سنوات"),
        ("BONUS", "پاداش"), ("EMPLOYER_INSURANCE", "بیمه سهم کارفرما"), ("SUPPLEMENTARY_INSURANCE", "بیمه تکمیلی"),
        ("NONCASH_BENEFIT", "مزایای غیرنقدی"), ("YEAR_END_BONUS", "عیدی"), ("UNUSED_LEAVE", "بازخرید مرخصی"),
        ("TERMINATION_BENEFIT", "مزایای پایان خدمت"), ("MARKETING_ADVERTISING", "تبلیغات و بازاریابی"),
        ("CONGRESS_EXHIBITION", "کنگره و نمایشگاه"), ("TRAVEL_MISSION", "سفر و ماموریت"),
        ("TRANSPORTATION", "حمل و نقل"), ("RESEARCH_LAB", "تحقیقات و آزمایشات"), ("MEMBERSHIP", "حق عضویت"),
        ("TRAINING", "آموزش"), ("BOARD_MEETING_FEE", "حق حضور جلسات"), ("RENT", "اجاره"),
        ("REPAIR_MAINTENANCE", "تعمیر و نگهداری"), ("UTILITIES", "آب، برق و انرژی"), ("TELECOM", "تلفن و ارتباطات"),
        ("ASSET_INSURANCE", "بیمه دارایی‌ها"), ("OFFICE_SUPPLIES", "ملزومات و لوازم مصرفی"),
        ("REGISTRATION_TRANSLATION", "ثبت، دفترخانه و دارالترجمه"), ("CONSULTING", "کارشناسی و مشاوره"),
        ("AUDIT_FINANCE_SERVICES", "حسابرسی و خدمات مالی"), ("SOFTWARE_INTERNET", "نرم‌افزار و اینترنت"),
        ("PUBLICATION_ADVERTISING", "آگهی و مطبوعات"), ("BANK_SERVICE_FEE", "کارمزد خدمات بانکی"),
        ("HOSPITALITY", "پذیرایی و تشریفات"), ("CLEANING", "نظافت"), ("FURNITURE_DEPRECIATION", "استهلاک اثاثیه"),
        ("SOFTWARE_DEPRECIATION", "استهلاک نرم‌افزار"), ("SCRAP_SALE", "درآمد فروش ضایعات"),
        ("FX_GAIN", "سود تسعیر ارز"), ("FX_LOSS", "زیان تسعیر ارز"), ("INVENTORY_SHORTAGE", "کسری موجودی"),
        ("IMPAIRMENT", "کاهش ارزش دارایی / موجودی"), ("EXPIRY_LOSS", "زیان انقضا"),
        ("FINANCE_INTEREST", "سود و کارمزد تسهیلات و هزینه مالی"), ("NON_OPERATING_INCOME", "سایر درآمد غیرعملیاتی"),
        ("NON_OPERATING_EXPENSE", "سایر هزینه غیرعملیاتی"), ("INCOME_TAX", "مالیات بر درآمد"), ("OTHER", "سایر")
    ];
}
