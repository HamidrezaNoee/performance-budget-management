using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Domain;

namespace PBM.Infrastructure;

public static class EnterpriseSeedData
{
    public static Task InitializeAsync(PbmDbContext db, bool includeWorkbookReferenceMembers, CancellationToken cancellationToken = default) =>
        InitializeAsync(db, cancellationToken);

    public static async Task InitializeAsync(PbmDbContext db, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(cancellationToken);
        if (tenant is null) return;

        if (!await db.Currencies.AnyAsync(x => x.TenantId == tenant.Id, cancellationToken))
        {
            db.Currencies.AddRange(
                new CurrencyDefinition { TenantId = tenant.Id, Code = "IRR", Name = "ریال ایران", Symbol = "ریال", IsBaseCurrency = true },
                new CurrencyDefinition { TenantId = tenant.Id, Code = "USD", Name = "دلار آمریکا", Symbol = "$" },
                new CurrencyDefinition { TenantId = tenant.Id, Code = "EUR", Name = "یورو", Symbol = "€" },
                new CurrencyDefinition { TenantId = tenant.Id, Code = "AED", Name = "درهم امارات", Symbol = "AED" },
                new CurrencyDefinition { TenantId = tenant.Id, Code = "CNY", Name = "یوان چین", Symbol = "¥" });
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
        await EnsureOperationalMasterDimensionsAsync(db, tenant.Id, cancellationToken);
        await SyncCurrencyDimensionMembersAsync(db, tenant.Id, cancellationToken);
        await EnsureEnterpriseBudgetModelsAsync(db, tenant.Id, cancellationToken);
        await EnsureTradeMeasuresAsync(db, tenant.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureOperationalMasterDimensionsAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var required = new (string Code, string Name, bool Hierarchical)[]
        {
            ("PRODUCT", "کالا / محصول", true),
            ("BRAND", "برند", true),
            ("UOM", "واحد سنجش", false),
            ("SUPPLIER", "تامین‌کننده", true),
            ("COUNTRY", "کشور", false),
            ("GEOGRAPHY", "موقعیت جغرافیایی", true),
            ("CURRENCY", "ارز", false),
            ("WAREHOUSE", "انبار", true),
            ("CUSTOMS", "گمرک / مبادی گمرکی", true),
            ("DEPARTMENT", "واحد سازمانی", true),
            ("COSTCENTER", "مرکز هزینه", true),
            ("ACCOUNT", "حساب", true),
            ("PROGRAM", "برنامه", true),
            ("ACTIVITY", "فعالیت", true),
            ("CUSTOMER", "مشتری", true),
            ("REGION", "منطقه", true),
            ("CONTRACT", "قرارداد", true),
            ("PROJECT", "پروژه", true),
            ("FUNDINGSOURCE", "منبع تامین مالی", true)
        };

        var existing = await db.Dimensions
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var item in required)
        {
            if (existing.ContainsKey(item.Code)) continue;
            var dimension = new DimensionDefinition
            {
                TenantId = tenantId,
                Code = item.Code,
                Name = item.Name,
                IsSystem = true,
                IsHierarchical = item.Hierarchical,
                IsActive = true
            };
            db.Dimensions.Add(dimension);
            existing[item.Code] = dimension;
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SyncCurrencyDimensionMembersAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var dimension = await db.Dimensions.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "CURRENCY", ct);
        if (dimension is null) return;

        var currencies = await db.Currencies.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(ct);
        var existing = await db.DimensionMembers.Where(x => x.DimensionId == dimension.Id && x.CompanyId == null).ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var currency in currencies)
        {
            var metadata = JsonSerializer.Serialize(new { currency.Symbol, currency.IsBaseCurrency });
            if (!existing.TryGetValue(currency.Code, out var member))
            {
                db.DimensionMembers.Add(new DimensionMember
                {
                    DimensionId = dimension.Id,
                    CompanyId = null,
                    Code = currency.Code,
                    Name = currency.Name,
                    ExternalKey = $"CURRENCY:{currency.Id}",
                    MetadataJson = metadata,
                    IsActive = true
                });
            }
            else
            {
                member.Name = currency.Name;
                member.MetadataJson = metadata;
                member.IsActive = true;
            }
        }
    }

    private static async Task EnsureTradeMeasuresAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var trade = await db.BudgetModels.Include(x => x.Measures).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "TRADE", ct);
        if (trade is null) return;
        var existing = trade.Measures.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = new[]
        {
            Measure(trade, "UNIT_COST", "بهای تمام‌شده واحد", "ریال", MeasureValueType.Rate, 16, MeasureAggregation.Average),
            Measure(trade, "GROSS_MARGIN_PERCENT", "درصد حاشیه سود", "%", MeasureValueType.Percentage, 17, MeasureAggregation.Average),
            Measure(trade, "PURCHASE_QTY", "تعداد خرید", "عدد", MeasureValueType.Quantity, 18),
            Measure(trade, "PURCHASE_FX", "مبلغ ارزی خرید", "ارز", MeasureValueType.Amount, 19),
            Measure(trade, "PURCHASE_IRR", "مبلغ ریالی خرید", "ریال", MeasureValueType.Amount, 20)
        };
        foreach (var measure in additions)
        {
            if (!existing.Contains(measure.Code)) db.Measures.Add(measure);
        }
    }

    private static async Task EnsureEnterpriseBudgetModelsAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var dimensions = await db.Dimensions.Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Code, ct);

        if (!await db.BudgetModels.AnyAsync(x => x.TenantId == tenantId && x.Code == "EXPENSE", ct)
            && dimensions.TryGetValue("DEPARTMENT", out var department)
            && dimensions.TryGetValue("COSTCENTER", out var costCenter)
            && dimensions.TryGetValue("ACCOUNT", out var account))
        {
            var model = new BudgetModel
            {
                TenantId = tenantId,
                Code = "EXPENSE",
                Name = "هزینه‌ها و درآمدهای عملیاتی",
                Description = "مدل عمومی بودجه ماهانه هزینه‌ها و درآمدها به تفکیک ابعاد سازمانی"
            };
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = department.Id, Sequence = 1 });
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = account.Id, Sequence = 2 });
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = costCenter.Id, Sequence = 3, IsRequired = false });
            model.Measures.Add(Measure(model, "EXPENSE_AMOUNT", "مبلغ هزینه / درآمد", "ریال", MeasureValueType.Amount, 1));
            db.BudgetModels.Add(model);
        }

        if (!await db.BudgetModels.AnyAsync(x => x.TenantId == tenantId && x.Code == "HR", ct) && dimensions.TryGetValue("DEPARTMENT", out department))
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

        if (!await db.BudgetModels.AnyAsync(x => x.TenantId == tenantId && x.Code == "FINSTAT", ct) && dimensions.TryGetValue("ACCOUNT", out account))
        {
            var model = new BudgetModel { TenantId = tenantId, Code = "FINSTAT", Name = "صورت‌های مالی و نسبت‌ها", Description = "صورت سود و زیان، ترازنامه، جریان نقدی و نسبت‌های مالی" };
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = account.Id, Sequence = 1 });
            model.Measures.Add(Measure(model, "STATEMENT_AMOUNT", "مبلغ صورت مالی", "ریال", MeasureValueType.Amount, 1));
            model.Measures.Add(Measure(model, "FINANCIAL_RATIO", "نسبت مالی", "%", MeasureValueType.Percentage, 2, MeasureAggregation.Average));
            db.BudgetModels.Add(model);
        }
    }

    private static MeasureDefinition Measure(BudgetModel model, string code, string name, string unit, MeasureValueType type, int order, MeasureAggregation aggregation = MeasureAggregation.Sum, string? formula = null) =>
        new() { BudgetModelId = model.Id, Code = code, Name = name, Unit = unit, ValueType = type, Aggregation = aggregation, DisplayOrder = order, IsCalculated = formula is not null, FormulaExpression = formula };
}
