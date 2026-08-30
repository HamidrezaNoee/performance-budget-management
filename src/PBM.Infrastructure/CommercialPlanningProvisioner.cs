using Microsoft.EntityFrameworkCore;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class CommercialPlanningProvisioner(PbmDbContext db)
{
    private static readonly IReadOnlyDictionary<string, (string Name, bool Hierarchical)> StandardDimensions =
        new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            ["PRODUCT"] = ("کالا / محصول", true),
            ["SUPPLIER"] = ("تامین‌کننده", true),
            ["BRAND"] = ("برند", true),
            ["CUSTOMER"] = ("مشتری", true),
            ["REGION"] = ("منطقه", true),
            ["DEPARTMENT"] = ("واحد سازمانی", true),
            ["COSTCENTER"] = ("مرکز هزینه", true),
            ["CONTRACT"] = ("قرارداد", true),
            ["CURRENCY"] = ("ارز", false),
            ["ACCOUNT"] = ("حساب", true),
            ["PROGRAM"] = ("برنامه", true),
            ["ACTIVITY"] = ("فعالیت", true),
            ["PROJECT"] = ("پروژه", true),
            ["FUNDINGSOURCE"] = ("منبع تامین مالی", false)
        };

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
        var dimensions = await EnsureStandardDimensionsAsync(tenantId, requestedCodes, ct);
        var attached = trade.Dimensions.Select(x => x.DimensionId).ToHashSet();
        var sequence = trade.Dimensions.Count == 0 ? 1 : trade.Dimensions.Max(x => x.Sequence) + 1;
        foreach (var code in requestedCodes)
        {
            var dimension = dimensions[code];
            if (attached.Contains(dimension.Id)) continue;
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
        EnsureMeasure(trade, "GROSS_SALES", "فروش ناخالص", "ریال", MeasureValueType.Amount, ref order,
            formula: "[SALES_QTY] * [SALES_PRICE]");
        EnsureMeasure(trade, "SALES_DISCOUNT", "تخفیفات ریالی فروش", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "SALES_RETURN", "برگشت از فروش", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "FOC_SALES_AMOUNT", "تخفیف / جایزه جنسی فروش", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "NET_SALES", "فروش خالص", "ریال", MeasureValueType.Amount, ref order,
            formula: "[GROSS_SALES] - [SALES_DISCOUNT] - [FOC_SALES_AMOUNT] - [SALES_RETURN]");
        EnsureMeasure(trade, "COGS_AMOUNT", "بهای تمام‌شده فروش عادی", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "FOC_COST", "بهای تمام‌شده جایزه جنسی", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "SALES_COGS_TOTAL", "جمع قیمت تمام‌شده کالای فروش‌رفته", "ریال", MeasureValueType.Amount, ref order,
            formula: "[COGS_AMOUNT] + [FOC_COST]");
        EnsureMeasure(trade, "PURCHASE_COMPANY_DISCOUNT", "تخفیف تأمین‌کننده (خرید)", "ریال", MeasureValueType.Amount, ref order);
        EnsureMeasure(trade, "SALES_GROSS_MARGIN", "سود ناخالص فروش", "ریال", MeasureValueType.Amount, ref order,
            formula: "[NET_SALES] - [SALES_COGS_TOTAL] + [PURCHASE_COMPANY_DISCOUNT]");

        ForceFormula(trade, "NET_SALES", "[GROSS_SALES] - [SALES_DISCOUNT] - [FOC_SALES_AMOUNT] - [SALES_RETURN]");
        ForceFormula(trade, "SALES_COGS_TOTAL", "[COGS_AMOUNT] + [FOC_COST]");
        ForceFormula(trade, "SALES_GROSS_MARGIN", "[NET_SALES] - [SALES_COGS_TOTAL] + [PURCHASE_COMPANY_DISCOUNT]");
        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureExpenseAsync(Guid tenantId, CancellationToken ct = default)
    {
        var expense = await db.BudgetModels.Include(x => x.Dimensions).Include(x => x.Measures)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "EXPENSE" && x.IsActive, ct)
            ?? throw new InvalidOperationException("EXPENSE budget model is not available.");

        var standardCodes = new[]
        {
            "DEPARTMENT", "ACCOUNT", "COSTCENTER", "PROGRAM", "ACTIVITY", "PROJECT",
            "FUNDINGSOURCE", "CONTRACT", "REGION"
        };
        var dimensions = await EnsureStandardDimensionsAsync(tenantId, standardCodes, ct);
        var expenseClass = await EnsureDimensionAsync(dimensions, tenantId, "EXPENSECLASS", "طبقه هزینه / درآمد", false, ct);
        var expenseItem = await EnsureDimensionAsync(dimensions, tenantId, "EXPENSEITEM", "ردیف هزینه / درآمد", false, ct);

        foreach (var (code, name) in ExpenseClasses)
            await EnsureMemberAsync(expenseClass, code, name, ct);
        foreach (var (code, name) in ExpenseItems)
            await EnsureMemberAsync(expenseItem, code, name, ct);

        var account = dimensions["ACCOUNT"];
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
            var dimension = dimensions[code];
            if (attached.Contains(dimension.Id)) continue;
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

    private async Task<Dictionary<string, DimensionDefinition>> EnsureStandardDimensionsAsync(
        Guid tenantId,
        IEnumerable<string> requestedCodes,
        CancellationToken ct)
    {
        var codes = requestedCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var dimensions = await db.Dimensions.Where(x => x.TenantId == tenantId && codes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var code in codes)
        {
            if (dimensions.TryGetValue(code, out var existing))
            {
                if (!existing.IsActive) existing.IsActive = true;
                continue;
            }
            if (!StandardDimensions.TryGetValue(code, out var definition))
                throw new InvalidOperationException($"Standard dimension definition '{code}' is missing.");
            var created = new DimensionDefinition
            {
                TenantId = tenantId,
                Code = code,
                Name = definition.Name,
                IsSystem = true,
                IsHierarchical = definition.Hierarchical,
                IsActive = true
            };
            db.Dimensions.Add(created);
            dimensions[code] = created;
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return dimensions;
    }

    private async Task<DimensionDefinition> EnsureDimensionAsync(
        IDictionary<string, DimensionDefinition> dimensions,
        Guid tenantId,
        string code,
        string name,
        bool hierarchical,
        CancellationToken ct)
    {
        if (dimensions.TryGetValue(code, out var existing))
        {
            if (!existing.IsActive) existing.IsActive = true;
            return existing;
        }
        var dimension = new DimensionDefinition
        {
            TenantId = tenantId,
            Code = code,
            Name = name,
            IsSystem = true,
            IsHierarchical = hierarchical,
            IsActive = true
        };
        db.Dimensions.Add(dimension);
        dimensions[code] = dimension;
        await db.SaveChangesAsync(ct);
        return dimension;
    }

    private async Task<DimensionMember> EnsureMemberAsync(
        DimensionDefinition dimension,
        string code,
        string name,
        CancellationToken ct,
        Guid? parentId = null)
    {
        var existing = await db.DimensionMembers.SingleOrDefaultAsync(x =>
            x.DimensionId == dimension.Id && x.Code == code && x.CompanyId == null, ct);
        if (existing is not null)
        {
            if (!existing.IsActive) existing.IsActive = true;
            return existing;
        }
        var member = new DimensionMember
        {
            DimensionId = dimension.Id,
            CompanyId = null,
            ParentId = parentId,
            Code = code,
            Name = name,
            IsActive = true
        };
        db.DimensionMembers.Add(member);
        await db.SaveChangesAsync(ct);
        return member;
    }

    private void EnsureMeasure(
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
        var measure = new MeasureDefinition
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
        };
        db.Measures.Add(measure);
        model.Measures.Add(measure);
    }

    private static void ForceFormula(BudgetModel model, string code, string formula)
    {
        var measure = model.Measures.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (measure is null) return;
        measure.IsCalculated = true;
        measure.FormulaExpression = formula;
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
        ("SALARY_BASE", "حقوق پایه"),
        ("FOOD_ALLOWANCE", "حق خواروبار"),
        ("HOUSING_ALLOWANCE", "حق مسکن"),
        ("CHILD_ALLOWANCE", "حق اولاد"),
        ("OVERTIME", "هزینه اضافه‌کاری"),
        ("MISSION", "حق ماموریت"),
        ("COMMUTE", "ایاب و ذهاب"),
        ("PHONE_ALLOWANCE", "کمک هزینه تلفن"),
        ("SENIORITY", "پایه سنوات"),
        ("BONUS", "هزینه پاداش"),
        ("EMPLOYER_INSURANCE", "هزینه بیمه سهم کارفرما"),
        ("SUPPLEMENTARY_INSURANCE", "بیمه تکمیلی سهم کارفرما"),
        ("MISSION_BENEFIT", "مزایای ماموریت"),
        ("NONCASH_BENEFIT", "هزینه‌های غیرنقدی کارکنان"),
        ("YEAR_END_BONUS", "هزینه عیدی"),
        ("UNUSED_LEAVE", "هزینه مرخصی استفاده‌نشده کارکنان"),
        ("TERMINATION_BENEFIT", "هزینه مزایای پایان خدمت کارکنان"),
        ("MARKETING_ADVERTISING", "هزینه تبلیغات و بازاریابی"),
        ("CONGRESS_EXHIBITION", "هزینه کنگره و نمایشگاه‌های تخصصی"),
        ("TRAVEL_MISSION", "هزینه سفر و ماموریت"),
        ("TRANSPORTATION", "هزینه حمل و نقل و ایاب و ذهاب"),
        ("RESEARCH_LAB", "هزینه تحقیقات و آزمایشات"),
        ("MEMBERSHIP", "هزینه حق عضویت‌ها"),
        ("TRAINING", "هزینه‌های آموزشی"),
        ("BOARD_MEETING_FEE", "هزینه حق حضور در جلسات"),
        ("RENT", "هزینه اجاره محل"),
        ("BUILDING_MAINTENANCE_CHARGE", "تعمیر و نگهداری و شارژ ساختمان"),
        ("FURNITURE_MAINTENANCE", "تعمیر و نگهداری اثاثه و منصوبات"),
        ("UTILITIES", "آب، برق و انرژی"),
        ("TELECOM", "تلفن و ارتباطات"),
        ("ASSET_INSURANCE", "بیمه دارایی‌ها"),
        ("OFFICE_SUPPLIES", "ملزومات مصرفی اداری و عمومی"),
        ("REGISTRATION_TRANSLATION", "ثبت، دفترخانه و دارالترجمه"),
        ("CONSULTING", "کارشناسی و حق‌المشاوره"),
        ("AUDIT_FINANCE_SERVICES", "حسابرسی و خدمات مالی"),
        ("SOFTWARE_INTERNET", "پشتیبانی نرم‌افزارها و اینترنت"),
        ("PUBLICATION_ADVERTISING", "آگهی و مطبوعات"),
        ("BANK_SERVICE_FEE", "کارمزد حواله‌ها و سایر خدمات بانکی"),
        ("HOSPITALITY", "پذیرایی کارکنان و تشریفات"),
        ("CLEANING", "نظافت و بهداشت"),
        ("SEMINAR_HOSPITALITY", "پذیرایی و تشریفات کنگره و سمینار"),
        ("FURNITURE_DEPRECIATION", "استهلاک اثاثیه و منصوبات"),
        ("SOFTWARE_DEPRECIATION", "استهلاک نرم‌افزارها"),
        ("SCRAP_SALE", "فروش ضایعات"),
        ("OPERATING_FX_GAIN", "سود ناشی از تسعیر دارایی‌ها و بدهی‌های ارزی عملیاتی"),
        ("INVENTORY_SURPLUS", "اضافی انبار"),
        ("OPERATING_EXCEPTIONAL_INCOME", "اقلام استثنایی عملیاتی - درآمد"),
        ("ACCOUNTING_POLICY_PRIOR_INCOME", "اثر سنواتی تغییر روش حسابداری - درآمد"),
        ("OTHER_OPERATING_INCOME", "سایر درآمد عملیاتی"),
        ("IMPAIRMENT", "زیان کاهش ارزش موجودی‌ها / سرمایه‌گذاری‌ها"),
        ("EXPIRY_LOSS", "زیان حاصل از تاریخ انقضای کالا"),
        ("OPERATING_FX_LOSS", "زیان ناشی از تسعیر دارایی‌ها و بدهی‌های ارزی عملیاتی"),
        ("INVENTORY_SHORTAGE", "کسری انبار"),
        ("ACCOUNTING_POLICY_PRIOR_EXPENSE", "اثر سنواتی تغییر روش حسابداری - هزینه"),
        ("OTHER_OPERATING_EXPENSE", "سایر هزینه عملیاتی"),
        ("FINANCE_INTEREST_IRR", "سود و کارمزد بانکی و تمدید وام‌ها - ریالی"),
        ("FINANCE_INTEREST", "سود و کارمزد بانکی و تمدید وام‌ها"),
        ("FINANCE_EXPERT", "کارشناسی مالی"),
        ("BANK_TRANSFER_FEE", "هزینه حواله‌های بانکی"),
        ("PROMISSORY_NOTE_CHEQUE", "خرید سفته / برات و صدور دسته چک"),
        ("GUARANTEE_REGISTRATION_STAMP", "حق ثبت و حق تمبر اسناد تضمینی"),
        ("GROUP_COMPANY_FINANCE_COST", "هزینه مالی قابل پرداخت به سایر شرکت‌های گروه"),
        ("ASSET_SALE_GAIN", "سود حاصل از فروش دارایی‌ها"),
        ("NON_OPERATING_FX_GAIN", "سود دارایی‌ها و بدهی‌های ارزی غیرمرتبط با عملیات"),
        ("FX_FUND_GAIN_LOSS", "سود / زیان تسعیر صندوق ارزی"),
        ("INVESTMENT_INTEREST_INCOME", "سود اوراق مشارکت / سپرده و سرمایه‌گذاری‌ها"),
        ("MARKETING_SAMPLE_REIMBURSEMENT", "دریافتی بابت نمونه بازاریابی"),
        ("NON_OPERATING_OTHER_COMPANY", "سایر درآمد از شرکت‌ها / اشخاص"),
        ("SHARE_SALE_LOSS", "زیان حاصل از فروش سهام"),
        ("NONCURRENT_ASSET_SALE_LOSS", "زیان حاصل از فروش دارایی‌های غیرجاری"),
        ("NON_OPERATING_FX_LOSS", "زیان دارایی‌ها و بدهی‌های ارزی غیرمرتبط با عملیات"),
        ("NON_OPERATING_EXCEPTIONAL_EXPENSE", "اقلام استثنایی غیرعملیاتی"),
        ("INCOME_TAX", "مالیات بر درآمد"),
        ("OTHER", "سایر")
    ];
}
