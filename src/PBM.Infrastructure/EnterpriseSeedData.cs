using Microsoft.EntityFrameworkCore;
using PBM.Domain;

namespace PBM.Infrastructure;

public static class EnterpriseSeedData
{
    public static async Task InitializeAsync(PbmDbContext db, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(cancellationToken);
        if (tenant is null) return;

        if (!await db.Currencies.AnyAsync(x => x.TenantId == tenant.Id, cancellationToken))
        {
            var irr = new CurrencyDefinition { TenantId = tenant.Id, Code = "IRR", Name = "ریال ایران", Symbol = "ریال", IsBaseCurrency = true };
            var usd = new CurrencyDefinition { TenantId = tenant.Id, Code = "USD", Name = "دلار آمریکا", Symbol = "$" };
            var eur = new CurrencyDefinition { TenantId = tenant.Id, Code = "EUR", Name = "یورو", Symbol = "€" };
            var aed = new CurrencyDefinition { TenantId = tenant.Id, Code = "AED", Name = "درهم امارات", Symbol = "AED" };
            var cny = new CurrencyDefinition { TenantId = tenant.Id, Code = "CNY", Name = "یوان چین", Symbol = "¥" };
            db.Currencies.AddRange(irr, usd, eur, aed, cny);
            db.FxRateSources.AddRange(
                new FxRateSource { TenantId = tenant.Id, Code = "MANUAL", Name = "نرخ ثبت دستی" },
                new FxRateSource { TenantId = tenant.Id, Code = "BUDGET", Name = "نرخ بودجه" },
                new FxRateSource { TenantId = tenant.Id, Code = "ACCOUNTING", Name = "نرخ حسابداری" });
        }

        if (!await db.Kpis.AnyAsync(x => x.TenantId == tenant.Id, cancellationToken))
        {
            db.Kpis.AddRange(
                new KpiDefinition { TenantId = tenant.Id, Code = "BUDGET_UTILIZATION", Name = "درصد تحقق / مصرف بودجه", Unit = "%", Weight = 25, Frequency = "Monthly", FormulaExpression = "[ACTUAL] / [BUDGET] * 100" },
                new KpiDefinition { TenantId = tenant.Id, Code = "FORECAST_ACCURACY", Name = "دقت پیش‌بینی", Unit = "%", Weight = 20, Frequency = "Monthly" },
                new KpiDefinition { TenantId = tenant.Id, Code = "SALES_ACHIEVEMENT", Name = "درصد تحقق فروش", Unit = "%", Weight = 30, Frequency = "Monthly", FormulaExpression = "[ACTUAL_SALES] / [TARGET_SALES] * 100" });
        }

        await db.SaveChangesAsync(cancellationToken);
        await EnsureEnterpriseBudgetModelsAsync(db, tenant.Id, cancellationToken);
        await EnsureWorkbookReferenceMembersAsync(db, tenant.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureEnterpriseBudgetModelsAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var dimensions = await db.Dimensions.Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Code, ct);

        if (!await db.BudgetModels.AnyAsync(x => x.TenantId == tenantId && x.Code == "EXPENSE", ct)
            && dimensions.TryGetValue("DEPARTMENT", out var department)
            && dimensions.TryGetValue("COSTCENTER", out var costCenter)
            && dimensions.TryGetValue("ACCOUNT", out var account))
        {
            var model = new BudgetModel { TenantId = tenantId, Code = "EXPENSE", Name = "هزینه‌های عملیاتی و ستادی", Description = "مدل بودجه ماهانه هزینه واحدها، مارکتینگ، فروش، اداری و پرسنلی" };
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = department.Id, Sequence = 1 });
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = account.Id, Sequence = 2 });
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = costCenter.Id, Sequence = 3, IsRequired = false });
            model.Measures.Add(Measure(model, "EXPENSE_AMOUNT", "مبلغ هزینه", "ریال", MeasureValueType.Amount, 1));
            db.BudgetModels.Add(model);
        }

        if (!await db.BudgetModels.AnyAsync(x => x.TenantId == tenantId && x.Code == "HR", ct)
            && dimensions.TryGetValue("DEPARTMENT", out department))
        {
            var model = new BudgetModel { TenantId = tenantId, Code = "HR", Name = "نیروی انسانی", Description = "تعداد نیروی انسانی و تغییرات ماهانه به تفکیک واحد" };
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = department.Id, Sequence = 1 });
            model.Measures.Add(Measure(model, "OPENING_HEADCOUNT", "تعداد ابتدای دوره", "نفر", MeasureValueType.Quantity, 1));
            model.Measures.Add(Measure(model, "HIRES", "افزایش نیرو", "نفر", MeasureValueType.Quantity, 2));
            model.Measures.Add(Measure(model, "TERMINATIONS", "کاهش نیرو", "نفر", MeasureValueType.Quantity, 3));
            model.Measures.Add(Measure(model, "CLOSING_HEADCOUNT", "تعداد پایان دوره", "نفر", MeasureValueType.Quantity, 4, formula: "[OPENING_HEADCOUNT] + [HIRES] - [TERMINATIONS]"));
            db.BudgetModels.Add(model);
        }

        if (!await db.BudgetModels.AnyAsync(x => x.TenantId == tenantId && x.Code == "FINANCE", ct)
            && dimensions.TryGetValue("ACCOUNT", out account)
            && dimensions.TryGetValue("CONTRACT", out var contract)
            && dimensions.TryGetValue("CURRENCY", out var currency))
        {
            var model = new BudgetModel { TenantId = tenantId, Code = "FINANCE", Name = "تامین مالی، مطالبات و بدهی‌ها", Description = "تسهیلات، بازپرداخت اصل و سود، مطالبات، بدهی و دریافت/پرداخت" };
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = account.Id, Sequence = 1 });
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = contract.Id, Sequence = 2, IsRequired = false });
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = currency.Id, Sequence = 3, IsRequired = false });
            model.Measures.Add(Measure(model, "FINANCE_AMOUNT", "مبلغ مالی", "ریال", MeasureValueType.Amount, 1));
            model.Measures.Add(Measure(model, "FINANCE_RATE", "نرخ", "%", MeasureValueType.Percentage, 2, MeasureAggregation.Average));
            db.BudgetModels.Add(model);
        }

        if (!await db.BudgetModels.AnyAsync(x => x.TenantId == tenantId && x.Code == "FINSTAT", ct)
            && dimensions.TryGetValue("ACCOUNT", out account))
        {
            var model = new BudgetModel { TenantId = tenantId, Code = "FINSTAT", Name = "صورت‌های مالی و نسبت‌ها", Description = "صورت سود و زیان، ترازنامه، جریان نقدی و نسبت‌های مالی" };
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = account.Id, Sequence = 1 });
            model.Measures.Add(Measure(model, "STATEMENT_AMOUNT", "مبلغ صورت مالی", "میلیون ریال", MeasureValueType.Amount, 1));
            model.Measures.Add(Measure(model, "FINANCIAL_RATIO", "نسبت مالی", "%", MeasureValueType.Percentage, 2, MeasureAggregation.Average));
            db.BudgetModels.Add(model);
        }
    }

    private static async Task EnsureWorkbookReferenceMembersAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var companyId = await db.Companies.Where(x => x.TenantId == tenantId).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var dimensions = await db.Dimensions.Where(x => x.TenantId == tenantId && (x.Code == "DEPARTMENT" || x.Code == "ACCOUNT")).ToDictionaryAsync(x => x.Code, ct);

        if (dimensions.TryGetValue("DEPARTMENT", out var department))
        {
            var existing = await db.DimensionMembers.Where(x => x.DimensionId == department.Id).Select(x => x.Code).ToHashSetAsync(ct);
            foreach (var (code, name) in new[]
            {
                ("MKT-HOSP", "مارکتینگ - بیمارستانی"), ("MKT-RX", "مارکتینگ - رکورداتی"), ("MEDICAL", "مدیکال"),
                ("MKT-EYE", "مارکتینگ - چشمی"), ("DIGITAL-MKT", "دیجیتال مارکتینگ"), ("MANAGEMENT", "مدیریت"),
                ("FINANCE", "مالی"), ("COMMERCIAL", "بازرگانی"), ("REGULATORY", "رگولاتوری"),
                ("HR", "منابع انسانی"), ("SALES", "فروش"), ("SALES-LENS", "فروش - لنز و رنیو")
            })
                if (!existing.Contains(code)) db.DimensionMembers.Add(new DimensionMember { DimensionId = department.Id, CompanyId = companyId, Code = code, Name = name });
        }

        if (dimensions.TryGetValue("ACCOUNT", out var account))
        {
            var existing = await db.DimensionMembers.Where(x => x.DimensionId == account.Id).Select(x => x.Code).ToHashSetAsync(ct);
            var accounts = new[]
            {
                ("SALARY_BASE", "حقوق پایه"), ("FOOD_ALLOWANCE", "حق خواروبار"), ("HOUSING_ALLOWANCE", "حق مسکن"),
                ("CHILD_ALLOWANCE", "حق اولاد"), ("OVERTIME", "هزینه اضافه کاری"), ("MISSION", "حق ماموریت"),
                ("TRANSPORT", "ایاب و ذهاب"), ("PHONE", "کمک هزینه تلفن"), ("SENIORITY", "پایه سنوات"),
                ("BONUS", "هزینه پاداش"), ("EMPLOYER_INSURANCE", "هزینه بیمه سهم کارفرما"), ("SUPPLEMENTARY_INSURANCE", "بیمه تکمیلی سهم کارفرما"),
                ("GROSS_SALES", "فروش ناخالص کالای تجاری"), ("SALES_DISCOUNT", "تخفیفات فروش"), ("NET_SALES", "فروش خالص"),
                ("COGS", "قیمت تمام شده کالای تجاری فروش رفته"), ("GROSS_PROFIT", "سود (زیان) ناخالص"), ("ADMIN_EXPENSE", "سایر هزینه های اداری و عمومی"),
                ("OPERATING_PROFIT", "سود (زیان) عملیاتی"), ("FINANCE_COST", "هزینه های مالی"), ("PROFIT_BEFORE_TAX", "سود (زیان) ویژه قبل از مالیات"),
                ("TAX", "مالیات"), ("NET_PROFIT", "سود خالص پس از کسر مالیات"),
                ("CASH_BANK", "موجودی نقد و بانک"), ("TRADE_RECEIVABLE", "حسابها و اسناد دریافتنی تجاری"), ("INVENTORY", "موجودی مواد و کالا"),
                ("CURRENT_ASSETS", "جمع داراییهای جاری"), ("TOTAL_ASSETS", "جمع داراییها"), ("TRADE_PAYABLE", "حسابها و اسناد پرداختنی تجاری"),
                ("CURRENT_LIABILITIES", "جمع بدهی های جاری"), ("EQUITY", "جمع حقوق صاحبان سهام"), ("TOTAL_LIAB_EQUITY", "جمع بدهیها و حقوق صاحبان سهام"),
                ("CFO", "جریان خالص نقد حاصل از فعالیت های عملیاتی"), ("CFI", "جریان خالص نقد حاصل از فعالیت های سرمایه گذاری"),
                ("CFF", "جریان خالص نقد حاصل از فعالیت های تامین مالی"), ("ENDING_CASH", "مانده موجودی نقد در پایان سال")
            };
            foreach (var (code, name) in accounts)
                if (!existing.Contains(code)) db.DimensionMembers.Add(new DimensionMember { DimensionId = account.Id, CompanyId = companyId, Code = code, Name = name });
        }
    }

    private static MeasureDefinition Measure(BudgetModel model, string code, string name, string unit, MeasureValueType type, int order, MeasureAggregation aggregation = MeasureAggregation.Sum, string? formula = null) =>
        new() { BudgetModelId = model.Id, Code = code, Name = name, Unit = unit, ValueType = type, Aggregation = aggregation, DisplayOrder = order, IsCalculated = formula is not null, FormulaExpression = formula };
}
