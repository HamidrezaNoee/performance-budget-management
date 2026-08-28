using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ScoredKpiService(PbmDbContext db, IUserContext user) : IKpiService
{
    public async Task<IReadOnlyList<KpiDto>> GetKpisAsync(CancellationToken cancellationToken = default) =>
        await db.Kpis.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId)
            .OrderBy(x => x.Code)
            .Select(x => new KpiDto(
                x.Id, x.Code, x.Name, x.Description, x.Unit, x.Weight,
                x.Minimum, x.Maximum, x.Frequency, x.FormulaExpression))
            .ToListAsync(cancellationToken);

    public async Task<KpiDto> CreateKpiAsync(
        CreateKpiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureKpiDefinitionEditor();
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("KPI code and name are required.");
        if (request.Weight is < 0m or > 100m)
            throw new ArgumentException("KPI weight must be between 0 and 100.");
        if (request.Minimum.HasValue && request.Maximum.HasValue && request.Minimum.Value > request.Maximum.Value)
            throw new ArgumentException("KPI minimum cannot be greater than maximum.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (code.Length > 80) throw new ArgumentException("KPI code cannot exceed 80 characters.");
        if (request.Name.Trim().Length > 240) throw new ArgumentException("KPI name cannot exceed 240 characters.");
        if (await db.Kpis.AnyAsync(x => x.TenantId == user.TenantId && x.Code == code, cancellationToken))
            throw new InvalidOperationException("A KPI with this code already exists.");

        var kpi = new KpiDefinition
        {
            TenantId = user.TenantId,
            Code = code,
            Name = request.Name.Trim(),
            Description = NormalizeOptional(request.Description, 2000),
            Unit = NormalizeOptional(request.Unit, 80),
            Weight = request.Weight,
            Minimum = request.Minimum,
            Maximum = request.Maximum,
            Frequency = NormalizeOptional(request.Frequency, 40) ?? "Monthly",
            FormulaExpression = NormalizeOptional(request.FormulaExpression, 2000)
        };
        db.Kpis.Add(kpi);
        AddAudit("KpiDefinition", kpi.Id, "CREATE", null, new
        {
            kpi.Code,
            kpi.Name,
            kpi.Weight,
            kpi.Minimum,
            kpi.Maximum,
            ScoreMode = ResolveScoreMode(kpi.Minimum, kpi.Maximum).ToString()
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(kpi);
    }

    public async Task<IReadOnlyList<KpiValueDto>> GetValuesAsync(
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, false, cancellationToken);
        if (!await db.FiscalYears.AsNoTracking().AnyAsync(
                x => x.Id == fiscalYearId && x.CompanyId == companyId,
                cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the company.");

        var values = await db.KpiValues.AsNoTracking()
            .Include(x => x.Kpi)
            .Include(x => x.Period)
            .Where(x => x.CompanyId == companyId
                && x.Period!.FiscalYearId == fiscalYearId
                && x.Kpi!.TenantId == user.TenantId)
            .OrderBy(x => x.Kpi!.Code)
            .ThenBy(x => x.Period!.Sequence)
            .ToListAsync(cancellationToken);

        return values.Select(ToValueDto).ToList();
    }

    public async Task<KpiValueDto> UpsertValueAsync(
        UpsertKpiValueRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(request.CompanyId, true, cancellationToken);
        var kpi = await db.Kpis.SingleOrDefaultAsync(
            x => x.Id == request.KpiId && x.TenantId == user.TenantId,
            cancellationToken)
            ?? throw new ArgumentException("KPI is invalid.");

        var period = await db.FiscalPeriods.Include(x => x.FiscalYear)
            .SingleOrDefaultAsync(x => x.Id == request.PeriodId, cancellationToken)
            ?? throw new ArgumentException("Fiscal period was not found.");
        if (period.FiscalYear?.CompanyId != request.CompanyId)
            throw new ArgumentException("Period does not belong to the company.");
        if (period.IsClosed || period.FiscalYear.IsClosed)
            throw new InvalidOperationException("Fiscal period is closed and KPI values cannot be changed.");

        var value = await db.KpiValues.SingleOrDefaultAsync(
            x => x.KpiId == request.KpiId
                && x.CompanyId == request.CompanyId
                && x.PeriodId == request.PeriodId,
            cancellationToken);
        var old = value is null ? null : new { value.Target, value.Actual, value.Score };
        if (value is null)
        {
            value = new KpiValue
            {
                KpiId = request.KpiId,
                CompanyId = request.CompanyId,
                PeriodId = request.PeriodId
            };
            db.KpiValues.Add(value);
        }

        var score = KpiScorePolicy.Evaluate(
            request.Target,
            request.Actual,
            kpi.Minimum,
            kpi.Maximum);
        value.Target = request.Target;
        value.Actual = request.Actual;
        value.Score = score.Score;
        value.UpdatedAtUtc = DateTime.UtcNow;

        AddAudit("KpiValue", value.Id, old is null ? "CREATE" : "UPDATE", old, new
        {
            value.Target,
            value.Actual,
            value.Score,
            ScoreMode = score.Mode.ToString(),
            score.IsOnTarget
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToValueDto(value, kpi);
    }

    private KpiValueDto ToValueDto(KpiValue value)
    {
        var kpi = value.Kpi ?? throw new InvalidOperationException("KPI value is missing its definition.");
        return ToValueDto(value, kpi);
    }

    private static KpiValueDto ToValueDto(KpiValue value, KpiDefinition kpi)
    {
        var score = KpiScorePolicy.Evaluate(value.Target, value.Actual, kpi.Minimum, kpi.Maximum);
        return new KpiValueDto(
            value.Id,
            value.KpiId,
            value.CompanyId,
            value.PeriodId,
            value.Target,
            value.Actual,
            score.Score,
            score.Score);
    }

    private static KpiDto ToDto(KpiDefinition kpi) => new(
        kpi.Id,
        kpi.Code,
        kpi.Name,
        kpi.Description,
        kpi.Unit,
        kpi.Weight,
        kpi.Minimum,
        kpi.Maximum,
        kpi.Frequency,
        kpi.FormulaExpression);

    private static KpiScoreMode ResolveScoreMode(decimal? minimum, decimal? maximum) =>
        minimum.HasValue && maximum.HasValue
            ? KpiScoreMode.TargetRange
            : maximum.HasValue
                ? KpiScoreMode.LowerIsBetter
                : KpiScoreMode.HigherIsBetter;

    private async Task EnsureCompanyAsync(Guid companyId, bool write, CancellationToken cancellationToken)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(
                x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive,
                cancellationToken))
            throw new UnauthorizedAccessException("Company is outside the current tenant.");
        if (user.IsInRole("SUPERADMIN")) return;
        if (!user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
        if (write && !user.CanWriteCompany(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private void EnsureKpiDefinitionEditor()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("BUDGET_MANAGER")) return;
        throw new UnauthorizedAccessException("Budget manager or administrator role is required to create KPI definitions.");
    }

    private void AddAudit(string entityType, Guid entityId, string action, object? oldValue, object? newValue) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Action = action,
            OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue)
        });

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
