using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public static class SeedData
{
    private static readonly string[] MonthNames = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    public static async Task InitializeAsync(PbmDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Tenants.AnyAsync(cancellationToken)) return;

        var tenant = new Tenant { Code = "DEMO", Name = "گروه نمونه" };
        var company = new Company { TenantId = tenant.Id, Code = "COMPANY-01", Name = "شرکت نمونه", Industry = "General" };
        tenant.Companies.Add(company);

        var subscription = new LicenseSubscription
        {
            TenantId = tenant.Id, LicenseKey = "DEMO-PBM-LICENSE", StartsAtUtc = DateTime.UtcNow.Date,
            ExpiresAtUtc = DateTime.UtcNow.Date.AddYears(1), MaxCompanies = 5, MaxUsers = 500
        };

        var pc = new PersianCalendar();
        var jalaliYear = pc.GetYear(DateTime.Today);
        var fiscalYear = new FiscalYear
        {
            CompanyId = company.Id,
            Code = jalaliYear.ToString(CultureInfo.InvariantCulture),
            Name = $"سال مالی {jalaliYear}",
            JalaliYear = jalaliYear,
            StartDate = pc.ToDateTime(jalaliYear, 1, 1, 0, 0, 0, 0),
            EndDate = pc.ToDateTime(jalaliYear, 12, pc.GetDaysInMonth(jalaliYear, 12), 23, 59, 59, 999)
        };
        for (var month = 1; month <= 12; month++)
        {
            var start = pc.ToDateTime(jalaliYear, month, 1, 0, 0, 0, 0);
            var days = pc.GetDaysInMonth(jalaliYear, month);
            var end = pc.ToDateTime(jalaliYear, month, days, 23, 59, 59, 999);
            fiscalYear.Periods.Add(new FiscalPeriod
            {
                FiscalYearId = fiscalYear.Id,
                Sequence = month,
                Code = $"{jalaliYear}-{month:00}",
                Name = MonthNames[month - 1],
                JalaliMonth = month,
                StartDate = start,
                EndDate = end
            });
        }

        var dimensions = new Dictionary<string, DimensionDefinition>();
        foreach (var (code, name) in new[]
        {
            ("PRODUCT", "کالا / محصول"), ("SUPPLIER", "تامین‌کننده"), ("DEPARTMENT", "واحد سازمانی"),
            ("COSTCENTER", "مرکز هزینه"), ("ACCOUNT", "حساب"), ("PROGRAM", "برنامه"), ("ACTIVITY", "فعالیت"),
            ("CURRENCY", "ارز"), ("BRAND", "برند"), ("CUSTOMER", "مشتری"), ("REGION", "منطقه"), ("CONTRACT", "قرارداد")
        })
        {
            dimensions[code] = new DimensionDefinition { TenantId = tenant.Id, Code = code, Name = name, IsSystem = true };
        }

        var product = dimensions["PRODUCT"];
        var supplier = dimensions["SUPPLIER"];
        var department = dimensions["DEPARTMENT"];
        var sampleProduct = new DimensionMember { DimensionId = product.Id, CompanyId = company.Id, Code = "ITEM-001", Name = "کالای نمونه" };
        var sampleSupplier = new DimensionMember { DimensionId = supplier.Id, CompanyId = company.Id, Code = "SUPPLIER-001", Name = "تأمین‌کننده نمونه" };
        var sampleDepartment = new DimensionMember { DimensionId = department.Id, CompanyId = company.Id, Code = "UNIT-01", Name = "واحد نمونه" };
        product.Members.Add(sampleProduct); supplier.Members.Add(sampleSupplier); department.Members.Add(sampleDepartment);

        var model = new BudgetModel
        {
            TenantId = tenant.Id,
            Code = "TRADE",
            Name = "خرید، فروش و موجودی",
            Description = "مدل عمومی برنامه‌ریزی خرید، فروش و موجودی برای کالاها و محصولات مختلف"
        };
        model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = product.Id, Sequence = 1 });
        model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = supplier.Id, Sequence = 2, IsRequired = false });

        var measures = new[]
        {
            Measure(model, "OPENING_QTY", "موجودی اول دوره", "عدد", MeasureValueType.Quantity, 1),
            Measure(model, "SALES_QTY", "تعداد فروش", "عدد", MeasureValueType.Quantity, 2),
            Measure(model, "FREE_SALES_QTY", "فروش رایگان / آفر", "عدد", MeasureValueType.Quantity, 3),
            Measure(model, "SALES_PRICE", "نرخ فروش", "ریال", MeasureValueType.Rate, 4, MeasureAggregation.Average),
            Measure(model, "GROSS_SALES", "فروش ناخالص", "ریال", MeasureValueType.Amount, 5, formula: "[SALES_QTY] * [SALES_PRICE]"),
            Measure(model, "IMPORT_QTY", "تعداد خرید / ورود", "عدد", MeasureValueType.Quantity, 6),
            Measure(model, "IMPORT_FX", "مبلغ ارزی خرید", "ارز", MeasureValueType.Amount, 7),
            Measure(model, "SAMPLE_QTY", "نمونه", "عدد", MeasureValueType.Quantity, 8),
            Measure(model, "WASTE_QTY", "ضایعات", "عدد", MeasureValueType.Quantity, 9),
            Measure(model, "CLOSING_QTY", "موجودی پایان دوره", "عدد", MeasureValueType.Quantity, 10, formula: "[OPENING_QTY] + [IMPORT_QTY] - [SALES_QTY] - [FREE_SALES_QTY] - [SAMPLE_QTY] - [WASTE_QTY]"),
            Measure(model, "CUSTOMS_VALUE", "ارزش مبنای خرید / گمرکی", "ریال", MeasureValueType.Amount, 11),
            Measure(model, "CUSTOMS_TARIFF", "حقوق و عوارض", "ریال", MeasureValueType.Amount, 12),
            Measure(model, "BANK_FEE", "کارمزد بانکی", "ریال", MeasureValueType.Amount, 13),
            Measure(model, "INSURANCE", "بیمه", "ریال", MeasureValueType.Amount, 14),
            Measure(model, "ORDER_REG_FEE", "سایر هزینه‌های سفارش", "ریال", MeasureValueType.Amount, 15)
        };
        foreach (var measure in measures) model.Measures.Add(measure);

        var scenario = new BudgetScenario { TenantId = tenant.Id, Code = "BASE", Name = "سناریوی پایه" };
        var plan = new BudgetPlan { CompanyId = company.Id, FiscalYearId = fiscalYear.Id, BudgetModelId = model.Id, Name = $"بودجه خرید و فروش {jalaliYear}" };
        var version = new BudgetVersion { BudgetPlanId = plan.Id, ScenarioId = scenario.Id, VersionNumber = 1, Name = "نسخه اولیه" };
        plan.Versions.Add(version);

        db.AddRange(tenant, subscription, fiscalYear);
        db.Dimensions.AddRange(dimensions.Values);
        db.AddRange(model, scenario, plan);
        await db.SaveChangesAsync(cancellationToken);

        var salesMeasure = measures.Single(x => x.Code == "GROSS_SALES");
        var coordinates = new[] { new DimensionSelection(product.Id, sampleProduct.Id), new DimensionSelection(supplier.Id, sampleSupplier.Id) };
        var hash = BudgetCoordinateKey.Create(coordinates);
        var json = System.Text.Json.JsonSerializer.Serialize(coordinates.OrderBy(x => x.DimensionId));
        var periods = fiscalYear.Periods.OrderBy(x => x.Sequence).Take(6).ToArray();
        for (var i = 0; i < periods.Length; i++)
        {
            AddFact(db, version.Id, periods[i].Id, salesMeasure.Id, ValueKind.Budget, 100_000_000_000m + i * 12_000_000_000m, "IRR", hash, json, coordinates);
            AddFact(db, version.Id, periods[i].Id, salesMeasure.Id, ValueKind.Actual, 82_000_000_000m + i * 10_000_000_000m, "IRR", hash, json, coordinates);
            AddFact(db, version.Id, periods[i].Id, salesMeasure.Id, ValueKind.Forecast, 98_000_000_000m + i * 11_000_000_000m, "IRR", hash, json, coordinates);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static MeasureDefinition Measure(BudgetModel model, string code, string name, string unit, MeasureValueType type, int order, MeasureAggregation aggregation = MeasureAggregation.Sum, string? formula = null) =>
        new() { BudgetModelId = model.Id, Code = code, Name = name, Unit = unit, ValueType = type, Aggregation = aggregation, DisplayOrder = order, IsCalculated = formula is not null, FormulaExpression = formula };

    private static void AddFact(PbmDbContext db, Guid versionId, Guid periodId, Guid measureId, ValueKind kind, decimal value, string currency, string hash, string json, IReadOnlyList<DimensionSelection> coordinates)
    {
        var fact = new BudgetFact { VersionId = versionId, PeriodId = periodId, MeasureId = measureId, ValueKind = kind, Value = value, CurrencyCode = currency, CoordinateHash = hash, CoordinatesJson = json, Source = "Seed" };
        foreach (var x in coordinates) fact.Dimensions.Add(new BudgetFactDimension { BudgetFactId = fact.Id, DimensionId = x.DimensionId, MemberId = x.MemberId });
        db.BudgetFacts.Add(fact);
    }
}
