using Microsoft.EntityFrameworkCore;
using PBM.Domain;

namespace PBM.Infrastructure;

public static class AssumptionSeedData
{
    private static readonly (string Code, string Name, string? Unit, string Description)[] StandardDefinitions =
    [
        ("FX_USD", "نرخ دلار بودجه", "IRR/USD", "نرخ تبدیل دلار به ریال برای محاسبات سناریویی بودجه."),
        ("FX_EUR", "نرخ یورو بودجه", "IRR/EUR", "نرخ تبدیل یورو به ریال برای محاسبات سناریویی بودجه."),
        ("FX_CNY", "نرخ یوان بودجه", "IRR/CNY", "نرخ تبدیل یوان چین به ریال برای محاسبات سناریویی بودجه."),
        ("FX_AED", "نرخ درهم بودجه", "IRR/AED", "نرخ تبدیل درهم امارات به ریال برای محاسبات سناریویی بودجه."),
        ("INFLATION_RATE", "نرخ تورم", "%", "فرض نرخ تورم مورد استفاده در هزینه‌ها و Forecast."),
        ("SALES_GROWTH_RATE", "نرخ رشد فروش", "%", "فرض رشد فروش نسبت به مبنای انتخاب‌شده."),
        ("SALARY_GROWTH_RATE", "نرخ افزایش حقوق", "%", "فرض رشد حقوق و مزایای کارکنان."),
        ("HEADCOUNT_GROWTH_RATE", "نرخ رشد تعداد کارکنان", "%", "فرض رشد Headcount در برنامه منابع انسانی."),
        ("CUSTOMS_RATE", "نرخ حقوق و عوارض گمرکی", "%", "نرخ یا ضریب پایه حقوق و عوارض گمرکی؛ در صورت نیاز برای گروه کالا در سطح Measure/Dimension تفکیک شود."),
        ("FREIGHT_GROWTH_RATE", "نرخ رشد هزینه حمل", "%", "فرض رشد هزینه حمل داخلی یا بین‌المللی."),
        ("FINANCE_RATE", "نرخ هزینه تأمین مالی", "%", "فرض نرخ هزینه پول/تأمین مالی برای برنامه‌ریزی مالی."),
        ("BAD_DEBT_RATE", "نرخ مطالبات مشکوک‌الوصول", "%", "فرض درصد مطالبات مشکوک‌الوصول برای برنامه‌ریزی و Forecast."),
        ("TAX_RATE", "نرخ مالیات مؤثر", "%", "فرض نرخ مالیات مؤثر برای برنامه‌ریزی صورت سود و زیان."),
        ("DISCOUNT_RATE", "نرخ تخفیف فروش", "%", "فرض نرخ میانگین تخفیف فروش."),
        ("WASTE_RATE", "نرخ ضایعات", "%", "فرض نرخ ضایعات برای برنامه‌ریزی موجودی و بهای تمام‌شده.")
    ];

    public static async Task InitializeAsync(PbmDbContext db, CancellationToken cancellationToken = default)
    {
        var tenants = await db.Tenants.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
        foreach (var tenantId in tenants)
        {
            var existing = await db.AssumptionDefinitions
                .Where(x => x.TenantId == tenantId)
                .Select(x => x.Code)
                .ToListAsync(cancellationToken);
            var existingCodes = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in StandardDefinitions)
            {
                if (existingCodes.Contains(item.Code)) continue;
                db.AssumptionDefinitions.Add(new AssumptionDefinition
                {
                    TenantId = tenantId,
                    Code = item.Code,
                    Name = item.Name,
                    Unit = item.Unit,
                    Description = item.Description,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
