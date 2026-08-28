using Microsoft.EntityFrameworkCore;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed record InitialTenantProvisioningOptions(
    string TenantCode,
    string TenantName,
    string CompanyCode,
    string CompanyName,
    string? Industry,
    string LicenseKey,
    DateTime LicenseStartsAtUtc,
    DateTime LicenseExpiresAtUtc,
    int MaxCompanies,
    int MaxUsers);

public static class InitialTenantProvisioner
{
    public static async Task InitializeAsync(
        PbmDbContext db,
        InitialTenantProvisioningOptions options,
        CancellationToken cancellationToken = default)
    {
        if (await db.Tenants.AnyAsync(cancellationToken)) return;

        Validate(options);
        var tenant = new Tenant
        {
            Code = NormalizeCode(options.TenantCode, "Tenant code"),
            Name = RequireName(options.TenantName, "Tenant name")
        };
        var company = new Company
        {
            TenantId = tenant.Id,
            Code = NormalizeCode(options.CompanyCode, "Company code"),
            Name = RequireName(options.CompanyName, "Company name"),
            Industry = string.IsNullOrWhiteSpace(options.Industry) ? null : options.Industry.Trim()
        };
        tenant.Companies.Add(company);

        var subscription = new LicenseSubscription
        {
            TenantId = tenant.Id,
            LicenseKey = options.LicenseKey.Trim(),
            StartsAtUtc = DateTime.SpecifyKind(options.LicenseStartsAtUtc, DateTimeKind.Utc),
            ExpiresAtUtc = DateTime.SpecifyKind(options.LicenseExpiresAtUtc, DateTimeKind.Utc),
            MaxCompanies = options.MaxCompanies,
            MaxUsers = options.MaxUsers,
            IsActive = true
        };

        var dimensions = CreateStandardDimensions(tenant.Id);
        var trade = CreateTradeModel(tenant.Id, dimensions);
        var baseScenario = new BudgetScenario { TenantId = tenant.Id, Code = "BASE", Name = "سناریوی پایه", IsActive = true };

        db.AddRange(tenant, subscription);
        db.Dimensions.AddRange(dimensions.Values);
        db.AddRange(trade, baseScenario);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, DimensionDefinition> CreateStandardDimensions(Guid tenantId)
    {
        var result = new Dictionary<string, DimensionDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name, hierarchical) in new[]
        {
            ("PRODUCT", "کالا / محصول", true),
            ("SUPPLIER", "تامین‌کننده", true),
            ("DEPARTMENT", "واحد سازمانی", true),
            ("COSTCENTER", "مرکز هزینه", true),
            ("ACCOUNT", "حساب", true),
            ("PROGRAM", "برنامه", true),
            ("ACTIVITY", "فعالیت", true),
            ("CURRENCY", "ارز", false),
            ("BRAND", "برند", true),
            ("CUSTOMER", "مشتری", true),
            ("REGION", "منطقه", true),
            ("CONTRACT", "قرارداد", true)
        })
        {
            result[code] = new DimensionDefinition
            {
                TenantId = tenantId,
                Code = code,
                Name = name,
                IsSystem = true,
                IsHierarchical = hierarchical
            };
        }
        return result;
    }

    private static BudgetModel CreateTradeModel(Guid tenantId, IReadOnlyDictionary<string, DimensionDefinition> dimensions)
    {
        var product = dimensions["PRODUCT"];
        var supplier = dimensions["SUPPLIER"];
        var model = new BudgetModel
        {
            TenantId = tenantId,
            Code = "TRADE",
            Name = "واردات، فروش و موجودی",
            Description = "مدل پایه برنامه‌ریزی واردات، فروش، موجودی و بهای تمام‌شده"
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
            Measure(model, "IMPORT_QTY", "تعداد واردات", "عدد", MeasureValueType.Quantity, 6),
            Measure(model, "IMPORT_FX", "مبلغ ارزی واردات", "ارز", MeasureValueType.Amount, 7),
            Measure(model, "SAMPLE_QTY", "نمونه", "عدد", MeasureValueType.Quantity, 8),
            Measure(model, "WASTE_QTY", "ضایعات", "عدد", MeasureValueType.Quantity, 9),
            Measure(model, "CLOSING_QTY", "موجودی پایان دوره", "عدد", MeasureValueType.Quantity, 10,
                formula: "[OPENING_QTY] + [IMPORT_QTY] - [SALES_QTY] - [FREE_SALES_QTY] - [SAMPLE_QTY] - [WASTE_QTY]"),
            Measure(model, "CUSTOMS_VALUE", "ارزش اظهار شده گمرکی", "ریال", MeasureValueType.Amount, 11),
            Measure(model, "CUSTOMS_TARIFF", "تعرفه گمرکی", "ریال", MeasureValueType.Amount, 12, formula: "[CUSTOMS_VALUE] * 0.05"),
            Measure(model, "BANK_FEE", "کارمزد بانکی", "ریال", MeasureValueType.Amount, 13),
            Measure(model, "INSURANCE", "بیمه", "ریال", MeasureValueType.Amount, 14),
            Measure(model, "ORDER_REG_FEE", "هزینه ثبت سفارش", "ریال", MeasureValueType.Amount, 15)
        };
        foreach (var measure in measures) model.Measures.Add(measure);
        return model;
    }

    private static MeasureDefinition Measure(
        BudgetModel model,
        string code,
        string name,
        string unit,
        MeasureValueType type,
        int order,
        MeasureAggregation aggregation = MeasureAggregation.Sum,
        string? formula = null) => new()
    {
        BudgetModelId = model.Id,
        Code = code,
        Name = name,
        Unit = unit,
        ValueType = type,
        Aggregation = aggregation,
        DisplayOrder = order,
        IsCalculated = formula is not null,
        FormulaExpression = formula
    };

    private static void Validate(InitialTenantProvisioningOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LicenseKey)) throw new ArgumentException("License key is required for initial provisioning.");
        if (options.LicenseStartsAtUtc >= options.LicenseExpiresAtUtc) throw new ArgumentException("License expiration must be after its start date.");
        if (options.MaxCompanies <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxCompanies));
        if (options.MaxUsers <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxUsers));
    }

    private static string NormalizeCode(string value, string field)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 64 || code.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')))
            throw new ArgumentException($"{field} must contain 2-64 letters, numbers, underscore, dash or dot characters.");
        return code;
    }

    private static string RequireName(string value, string field)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 200) throw new ArgumentException($"{field} is required and must be at most 200 characters.");
        return name;
    }
}
