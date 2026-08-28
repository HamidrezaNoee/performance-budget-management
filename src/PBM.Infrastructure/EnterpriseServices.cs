using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ReferenceDataService(PbmDbContext db, IUserContext user) : IReferenceDataService
{
    public async Task<IReadOnlyList<CurrencyDto>> GetCurrenciesAsync(CancellationToken cancellationToken = default) =>
        await db.Currencies.AsNoTracking().Where(x => x.TenantId == user.TenantId && x.IsActive).OrderByDescending(x => x.IsBaseCurrency).ThenBy(x => x.Code)
            .Select(x => new CurrencyDto(x.Id, x.Code, x.Name, x.Symbol, x.IsBaseCurrency, x.IsActive)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FxRateSourceDto>> GetFxRateSourcesAsync(CancellationToken cancellationToken = default) =>
        await db.FxRateSources.AsNoTracking().Where(x => x.TenantId == user.TenantId && x.IsActive).OrderBy(x => x.Name)
            .Select(x => new FxRateSourceDto(x.Id, x.Code, x.Name, x.IsActive)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FxRateDto>> GetFxRatesAsync(DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        var query = db.FxRates.AsNoTracking().Where(x => x.Source!.TenantId == user.TenantId);
        if (fromDate.HasValue) query = query.Where(x => x.RateDate >= fromDate.Value.Date);
        if (toDate.HasValue) query = query.Where(x => x.RateDate < toDate.Value.Date.AddDays(1));
        return await query.OrderByDescending(x => x.RateDate).ThenBy(x => x.FromCurrency!.Code)
            .Select(x => new FxRateDto(x.Id, x.SourceId, x.Source!.Name, x.FromCurrencyId, x.FromCurrency!.Code, x.ToCurrencyId, x.ToCurrency!.Code, x.RateDate, x.Rate, x.Note))
            .ToListAsync(cancellationToken);
    }

    public async Task<FxRateDto> UpsertFxRateAsync(UpsertFxRateRequest request, CancellationToken cancellationToken = default)
    {
        EnsureFinanceEditor();
        if (request.Rate <= 0) throw new ArgumentException("FX rate must be greater than zero.");
        if (request.FromCurrencyId == request.ToCurrencyId) throw new ArgumentException("Source and destination currencies must be different.");
        var source = await db.FxRateSources.SingleAsync(x => x.Id == request.SourceId && x.TenantId == user.TenantId && x.IsActive, cancellationToken);
        var currencies = await db.Currencies.Where(x => x.TenantId == user.TenantId && x.IsActive && (x.Id == request.FromCurrencyId || x.Id == request.ToCurrencyId)).ToListAsync(cancellationToken);
        if (currencies.Count != 2) throw new ArgumentException("One or more currencies are invalid.");
        var from = currencies.Single(x => x.Id == request.FromCurrencyId); var to = currencies.Single(x => x.Id == request.ToCurrencyId);
        FxRate rate;
        decimal? oldRate = null;
        if (request.Id.HasValue)
        {
            rate = await db.FxRates.SingleAsync(x => x.Id == request.Id.Value && x.Source!.TenantId == user.TenantId, cancellationToken); oldRate = rate.Rate;
            rate.SourceId = request.SourceId; rate.FromCurrencyId = request.FromCurrencyId; rate.ToCurrencyId = request.ToCurrencyId; rate.RateDate = request.RateDate.Date; rate.Rate = request.Rate; rate.Note = request.Note; rate.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            rate = new FxRate { SourceId = request.SourceId, FromCurrencyId = request.FromCurrencyId, ToCurrencyId = request.ToCurrencyId, RateDate = request.RateDate.Date, Rate = request.Rate, Note = request.Note }; db.FxRates.Add(rate);
        }
        db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId, UserId = user.UserId == Guid.Empty ? null : user.UserId, EntityType = "FxRate", EntityId = rate.Id.ToString(), Action = oldRate.HasValue ? "UPDATE" : "CREATE", OldValueJson = oldRate.HasValue ? JsonSerializer.Serialize(new { Rate = oldRate.Value }) : null, NewValueJson = JsonSerializer.Serialize(new { rate.Rate, rate.RateDate, from = from.Code, to = to.Code, source = source.Code }) });
        await db.SaveChangesAsync(cancellationToken);
        return new FxRateDto(rate.Id, source.Id, source.Name, from.Id, from.Code, to.Id, to.Code, rate.RateDate, rate.Rate, rate.Note);
    }

    public async Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, DateTime rateDate, Guid? sourceId = null, CancellationToken cancellationToken = default)
    {
        if (fromCurrency.Equals(toCurrency, StringComparison.OrdinalIgnoreCase)) return amount;
        var query = db.FxRates.AsNoTracking().Where(x => x.Source!.TenantId == user.TenantId && x.FromCurrency!.Code == fromCurrency && x.ToCurrency!.Code == toCurrency && x.RateDate <= rateDate.Date);
        if (sourceId.HasValue) query = query.Where(x => x.SourceId == sourceId.Value);
        var rate = await query.OrderByDescending(x => x.RateDate).Select(x => (decimal?)x.Rate).FirstOrDefaultAsync(cancellationToken);
        if (!rate.HasValue) throw new KeyNotFoundException($"No FX rate found for {fromCurrency}/{toCurrency} on or before {rateDate:yyyy-MM-dd}.");
        return amount * rate.Value;
    }

    private void EnsureFinanceEditor()
    {
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN") && !user.IsInRole("CFO") && !user.IsInRole("BUDGET_MANAGER"))
            throw new UnauthorizedAccessException("CFO, budget manager or administrator role is required to maintain FX rates.");
    }
}

public sealed class KpiService(PbmDbContext db, IUserContext user) : IKpiService
{
    public async Task<IReadOnlyList<KpiDto>> GetKpisAsync(CancellationToken cancellationToken = default) =>
        await db.Kpis.AsNoTracking().Where(x => x.TenantId == user.TenantId).OrderBy(x => x.Code)
            .Select(x => new KpiDto(x.Id, x.Code, x.Name, x.Description, x.Unit, x.Weight, x.Minimum, x.Maximum, x.Frequency, x.FormulaExpression)).ToListAsync(cancellationToken);

    public async Task<KpiDto> CreateKpiAsync(CreateKpiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureKpiDefinitionEditor();
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("KPI code and name are required.");
        if (request.Weight < 0 || request.Weight > 100) throw new ArgumentException("KPI weight must be between 0 and 100.");
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Kpis.AnyAsync(x => x.TenantId == user.TenantId && x.Code == code, cancellationToken)) throw new InvalidOperationException("A KPI with this code already exists.");
        var kpi = new KpiDefinition { TenantId = user.TenantId, Code = code, Name = request.Name.Trim(), Description = request.Description, Unit = request.Unit, Weight = request.Weight, Minimum = request.Minimum, Maximum = request.Maximum, Frequency = string.IsNullOrWhiteSpace(request.Frequency) ? "Monthly" : request.Frequency, FormulaExpression = request.FormulaExpression };
        db.Kpis.Add(kpi); db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId, UserId = user.UserId == Guid.Empty ? null : user.UserId, EntityType = "KpiDefinition", EntityId = kpi.Id.ToString(), Action = "CREATE", NewValueJson = JsonSerializer.Serialize(new { kpi.Code, kpi.Name, kpi.Weight }) });
        await db.SaveChangesAsync(cancellationToken);
        return new KpiDto(kpi.Id, kpi.Code, kpi.Name, kpi.Description, kpi.Unit, kpi.Weight, kpi.Minimum, kpi.Maximum, kpi.Frequency, kpi.FormulaExpression);
    }

    public async Task<IReadOnlyList<KpiValueDto>> GetValuesAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, false, cancellationToken);
        if (!await db.FiscalYears.AnyAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken)) throw new ArgumentException("Fiscal year does not belong to the company.");
        return await db.KpiValues.AsNoTracking().Where(x => x.CompanyId == companyId && x.Period!.FiscalYearId == fiscalYearId && x.Kpi!.TenantId == user.TenantId).OrderBy(x => x.Kpi!.Code).ThenBy(x => x.Period!.Sequence)
            .Select(x => new KpiValueDto(x.Id, x.KpiId, x.CompanyId, x.PeriodId, x.Target, x.Actual, x.Score, x.Target == 0 ? 0 : Math.Round(x.Actual / x.Target * 100, 2)))
            .ToListAsync(cancellationToken);
    }

    public async Task<KpiValueDto> UpsertValueAsync(UpsertKpiValueRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(request.CompanyId, true, cancellationToken);
        if (!await db.Kpis.AnyAsync(x => x.Id == request.KpiId && x.TenantId == user.TenantId, cancellationToken)) throw new ArgumentException("KPI is invalid.");
        var period = await db.FiscalPeriods.Include(x => x.FiscalYear).SingleAsync(x => x.Id == request.PeriodId, cancellationToken);
        if (period.FiscalYear!.CompanyId != request.CompanyId) throw new ArgumentException("Period does not belong to the company.");
        if (period.IsClosed || period.FiscalYear.IsClosed) throw new InvalidOperationException("Fiscal period is closed and KPI values cannot be changed.");
        var value = await db.KpiValues.SingleOrDefaultAsync(x => x.KpiId == request.KpiId && x.CompanyId == request.CompanyId && x.PeriodId == request.PeriodId, cancellationToken);
        var old = value is null ? null : JsonSerializer.Serialize(new { value.Target, value.Actual, value.Score });
        if (value is null) { value = new KpiValue { KpiId = request.KpiId, CompanyId = request.CompanyId, PeriodId = request.PeriodId }; db.KpiValues.Add(value); }
        value.Target = request.Target; value.Actual = request.Actual; value.Score = request.Target == 0 ? 0 : Math.Round(request.Actual / request.Target * 100, 2); value.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId, UserId = user.UserId == Guid.Empty ? null : user.UserId, EntityType = "KpiValue", EntityId = value.Id.ToString(), Action = old is null ? "CREATE" : "UPDATE", OldValueJson = old, NewValueJson = JsonSerializer.Serialize(new { value.Target, value.Actual, value.Score }) });
        await db.SaveChangesAsync(cancellationToken);
        return new KpiValueDto(value.Id, value.KpiId, value.CompanyId, value.PeriodId, value.Target, value.Actual, value.Score, value.Target == 0 ? 0 : Math.Round(value.Actual / value.Target * 100, 2));
    }

    private async Task EnsureCompanyAsync(Guid companyId, bool write, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct)) throw new UnauthorizedAccessException("Company is outside the current tenant.");
        if (user.IsInRole("SUPERADMIN")) return;
        if (!user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
        if (write && !user.CanWriteCompany(companyId)) throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private void EnsureKpiDefinitionEditor()
    {
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN") && !user.IsInRole("BUDGET_MANAGER"))
            throw new UnauthorizedAccessException("Budget manager or administrator role is required to create KPI definitions.");
    }
}

public sealed class AuditService(PbmDbContext db, IUserContext user) : IAuditService
{
    public async Task<IReadOnlyList<AuditLogDto>> GetRecentAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN") && !user.IsInRole("AUDITOR") && !user.IsInRole("CFO") && !user.IsInRole("BUDGET_MANAGER"))
            throw new UnauthorizedAccessException("Audit access requires auditor, finance, budget manager or administrator role.");
        take = Math.Clamp(take, 1, 500);
        return await db.AuditLogs.AsNoTracking().Where(x => x.TenantId == user.TenantId).OrderByDescending(x => x.CreatedAtUtc).Take(take)
            .Select(x => new AuditLogDto(x.Id, x.UserId, x.EntityType, x.EntityId, x.Action, x.OldValueJson, x.NewValueJson, x.IpAddress, x.CreatedAtUtc)).ToListAsync(cancellationToken);
    }
}
