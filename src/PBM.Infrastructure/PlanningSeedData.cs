using Microsoft.EntityFrameworkCore;
using PBM.Domain;

namespace PBM.Infrastructure;

public static class PlanningSeedData
{
    public static async Task InitializeAsync(PbmDbContext db, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(cancellationToken);
        if (tenant is null) return;

        await EnsureDimensionsAsync(db, tenant.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await EnsureScenariosAsync(db, tenant.Id, cancellationToken);
        await EnsureExpenseModelDimensionsAsync(db, tenant.Id, cancellationToken);
        await EnsureTradeLandedCostMeasuresAsync(db, tenant.Id, cancellationToken);
        await EnsureDriverBasedMeasuresAsync(db, tenant.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDimensionsAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Dimensions.Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);

        var required = new[]
        {
            (Code: "COSTTYPE", Name: "نوع هزینه", Hierarchical: false),
            (Code: "PROJECT", Name: "پروژه", Hierarchical: true),
            (Code: "FUNDINGSOURCE", Name: "منبع تامین مالی", Hierarchical: false)
        };

        foreach (var item in required)
        {
            if (existing.ContainsKey(item.Code)) continue;
            var dimension = new DimensionDefinition
            {
                TenantId = tenantId,
                Code = item.Code,
                Name = item.Name,
                IsSystem = true,
                IsHierarchical = item.Hierarchical
            };
            db.Dimensions.Add(dimension);
            existing[item.Code] = dimension;
        }

        var costType = existing["COSTTYPE"];
        await EnsureMemberAsync(db, costType, "OPEX", "هزینه عملیاتی", ct);
        await EnsureMemberAsync(db, costType, "CAPEX", "هزینه سرمایه‌ای", ct);

        var fundingSource = existing["FUNDINGSOURCE"];
        await EnsureMemberAsync(db, fundingSource, "OPERATING_CASH", "جریان نقد عملیاتی", ct);
        await EnsureMemberAsync(db, fundingSource, "BANK_LOAN", "تسهیلات بانکی", ct);
        await EnsureMemberAsync(db, fundingSource, "SHAREHOLDER", "تامین مالی سهامداران", ct);
        await EnsureMemberAsync(db, fundingSource, "OTHER", "سایر منابع", ct);
    }

    private static async Task EnsureScenariosAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.BudgetScenarios.Where(x => x.TenantId == tenantId).Select(x => x.Code).ToHashSetAsync(ct);
        foreach (var (code, name) in new[]
        {
            ("OPTIMISTIC", "سناریوی خوش‌بینانه"),
            ("PESSIMISTIC", "سناریوی بدبینانه"),
            ("STRESS", "سناریوی تنش"),
            ("LATEST_FORECAST", "آخرین پیش‌بینی")
        })
        {
            if (!existing.Contains(code)) db.BudgetScenarios.Add(new BudgetScenario { TenantId = tenantId, Code = code, Name = name });
        }
    }

    private static async Task EnsureExpenseModelDimensionsAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var expense = await db.BudgetModels.Include(x => x.Dimensions).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "EXPENSE", ct);
        if (expense is null) return;

        var dimensions = await db.Dimensions.Where(x => x.TenantId == tenantId && (x.Code == "COSTTYPE" || x.Code == "PROJECT" || x.Code == "PROGRAM" || x.Code == "ACTIVITY"))
            .ToDictionaryAsync(x => x.Code, ct);
        var attached = expense.Dimensions.Select(x => x.DimensionId).ToHashSet();
        var nextSequence = expense.Dimensions.Count == 0 ? 1 : expense.Dimensions.Max(x => x.Sequence) + 1;

        foreach (var code in new[] { "COSTTYPE", "PROGRAM", "ACTIVITY", "PROJECT" })
        {
            if (!dimensions.TryGetValue(code, out var dimension) || attached.Contains(dimension.Id)) continue;
            db.BudgetModelDimensions.Add(new BudgetModelDimension
            {
                BudgetModelId = expense.Id,
                DimensionId = dimension.Id,
                Sequence = nextSequence++,
                IsRequired = false
            });
            attached.Add(dimension.Id);
        }
    }

    private static async Task EnsureTradeLandedCostMeasuresAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var trade = await db.BudgetModels.Include(x => x.Measures).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "TRADE", ct);
        if (trade is null) return;
        var existing = trade.Measures.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = trade.Measures.Count == 0 ? 1 : trade.Measures.Max(x => x.DisplayOrder) + 1;

        Add("FREIGHT_IRR", "هزینه حمل بین‌المللی", "ریال", MeasureValueType.Amount);
        Add("CLEARANCE_FEE", "هزینه ترخیص", "ریال", MeasureValueType.Amount);
        Add("INLAND_TRANSPORT", "حمل داخلی", "ریال", MeasureValueType.Amount);
        Add("OTHER_IMPORT_COST", "سایر هزینه‌های واردات", "ریال", MeasureValueType.Amount);
        Add("LANDED_COST_TOTAL", "بهای تمام‌شده واردات", "ریال", MeasureValueType.Amount,
            formula: "[CUSTOMS_VALUE] + [CUSTOMS_TARIFF] + [BANK_FEE] + [INSURANCE] + [ORDER_REG_FEE] + [FREIGHT_IRR] + [CLEARANCE_FEE] + [INLAND_TRANSPORT] + [OTHER_IMPORT_COST]");
        Add("LANDED_COST_PER_UNIT", "بهای تمام‌شده واردات هر واحد", "ریال", MeasureValueType.Rate, MeasureAggregation.Average,
            "[LANDED_COST_TOTAL] / [IMPORT_QTY]");
        Add("GROSS_MARGIN_AMOUNT", "حاشیه سود ناخالص", "ریال", MeasureValueType.Amount,
            formula: "[GROSS_SALES] - [LANDED_COST_TOTAL]");
        Add("GROSS_MARGIN_PERCENT_CALC", "درصد حاشیه سود محاسباتی", "%", MeasureValueType.Percentage, MeasureAggregation.Average,
            "[GROSS_MARGIN_AMOUNT] / [GROSS_SALES] * 100");

        void Add(string code, string name, string unit, MeasureValueType valueType, MeasureAggregation aggregation = MeasureAggregation.Sum, string? formula = null)
        {
            if (existing.Contains(code)) return;
            db.Measures.Add(new MeasureDefinition
            {
                BudgetModelId = trade.Id,
                Code = code,
                Name = name,
                Unit = unit,
                ValueType = valueType,
                Aggregation = aggregation,
                IsCalculated = formula is not null,
                FormulaExpression = formula,
                DisplayOrder = order++
            });
            existing.Add(code);
        }
    }

    private static async Task EnsureDriverBasedMeasuresAsync(PbmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var models = await db.BudgetModels.Include(x => x.Measures)
            .Where(x => x.TenantId == tenantId && (x.Code == "EXPENSE" || x.Code == "HR" || x.Code == "FINANCE"))
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);

        if (models.TryGetValue("EXPENSE", out var expense))
        {
            AddMeasure(db, expense, "DRIVER_VOLUME", "مقدار محرک بودجه", "واحد", MeasureValueType.Quantity);
            AddMeasure(db, expense, "DRIVER_RATE", "نرخ هر واحد محرک", "ریال", MeasureValueType.Rate, MeasureAggregation.Average);
            AddMeasure(db, expense, "DRIVER_BUDGET_AMOUNT", "بودجه مبتنی بر محرک", "ریال", MeasureValueType.Amount,
                formula: "[DRIVER_VOLUME] * [DRIVER_RATE]");
        }

        if (models.TryGetValue("HR", out var hr))
        {
            AddMeasure(db, hr, "AVERAGE_HEADCOUNT", "متوسط تعداد نیروی انسانی", "نفر", MeasureValueType.Quantity, MeasureAggregation.Average,
                "([OPENING_HEADCOUNT] + [CLOSING_HEADCOUNT]) / 2");
            AddMeasure(db, hr, "AVG_COST_PER_EMPLOYEE", "متوسط هزینه هر نفر", "ریال", MeasureValueType.Rate, MeasureAggregation.Average);
            AddMeasure(db, hr, "PERSONNEL_COST", "بودجه هزینه پرسنلی", "ریال", MeasureValueType.Amount,
                formula: "[AVERAGE_HEADCOUNT] * [AVG_COST_PER_EMPLOYEE]");
        }

        if (models.TryGetValue("FINANCE", out var finance))
        {
            AddMeasure(db, finance, "OPENING_DEBT", "مانده تسهیلات ابتدای دوره", "ریال", MeasureValueType.Amount, MeasureAggregation.LastNonEmpty);
            AddMeasure(db, finance, "DRAWDOWN", "دریافت تسهیلات", "ریال", MeasureValueType.Amount);
            AddMeasure(db, finance, "PRINCIPAL_REPAYMENT", "بازپرداخت اصل", "ریال", MeasureValueType.Amount);
            AddMeasure(db, finance, "CLOSING_DEBT", "مانده تسهیلات پایان دوره", "ریال", MeasureValueType.Amount, MeasureAggregation.LastNonEmpty,
                "[OPENING_DEBT] + [DRAWDOWN] - [PRINCIPAL_REPAYMENT]");
            AddMeasure(db, finance, "AVERAGE_DEBT", "متوسط مانده تسهیلات", "ریال", MeasureValueType.Amount, MeasureAggregation.Average,
                "([OPENING_DEBT] + [CLOSING_DEBT]) / 2");
            AddMeasure(db, finance, "INTEREST_EXPENSE", "هزینه سود ماهانه", "ریال", MeasureValueType.Amount,
                formula: "[AVERAGE_DEBT] * [FINANCE_RATE] / 1200");
        }
    }

    private static void AddMeasure(
        PbmDbContext db,
        BudgetModel model,
        string code,
        string name,
        string unit,
        MeasureValueType valueType,
        MeasureAggregation aggregation = MeasureAggregation.Sum,
        string? formula = null)
    {
        if (model.Measures.Any(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase))) return;
        var order = model.Measures.Count == 0 ? 1 : model.Measures.Max(x => x.DisplayOrder) + 1;
        var measure = new MeasureDefinition
        {
            BudgetModelId = model.Id,
            Code = code,
            Name = name,
            Unit = unit,
            ValueType = valueType,
            Aggregation = aggregation,
            IsCalculated = formula is not null,
            FormulaExpression = formula,
            DisplayOrder = order
        };
        db.Measures.Add(measure);
        model.Measures.Add(measure);
    }

    private static async Task EnsureMemberAsync(PbmDbContext db, DimensionDefinition dimension, string code, string name, CancellationToken ct)
    {
        if (await db.DimensionMembers.AnyAsync(x => x.DimensionId == dimension.Id && x.CompanyId == null && x.Code == code, ct)) return;
        var member = new DimensionMember { DimensionId = dimension.Id, Code = code, Name = name };
        db.DimensionMembers.Add(member);
        dimension.Members.Add(member);
    }
}
