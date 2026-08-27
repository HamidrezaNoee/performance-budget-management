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
    }
}
