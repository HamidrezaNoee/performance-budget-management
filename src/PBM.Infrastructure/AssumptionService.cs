using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class AssumptionService(
    PbmDbContext db,
    IUserContext user,
    ICalculationService calculation) : IAssumptionService
{
    public async Task<IReadOnlyList<AssumptionDefinitionDto>> GetDefinitionsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.AssumptionDefinitions.AsNoTracking().Where(x => x.TenantId == user.TenantId);
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Name)
            .Select(x => new AssumptionDefinitionDto(x.Id, x.Code, x.Name, x.Unit, x.Description, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<AssumptionDefinitionDto> CreateDefinitionAsync(
        CreateAssumptionDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureDefinitionManager();
        var code = NormalizeCode(request.Code);
        var name = NormalizeRequired(request.Name, "Assumption name", 200);
        if (await db.AssumptionDefinitions.AnyAsync(x => x.TenantId == user.TenantId && x.Code == code, cancellationToken))
            throw new InvalidOperationException("An assumption with this code already exists.");

        var entity = new AssumptionDefinition
        {
            TenantId = user.TenantId,
            Code = code,
            Name = name,
            Unit = NormalizeOptional(request.Unit, 50),
            Description = NormalizeOptional(request.Description, 1000),
            IsActive = true
        };
        db.AssumptionDefinitions.Add(entity);
        AddAudit("AssumptionDefinition", entity.Id, "CREATE", new { entity.Code, entity.Name, entity.Unit, entity.Description });
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<AssumptionDefinitionDto> UpdateDefinitionAsync(
        Guid definitionId,
        UpdateAssumptionDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureDefinitionManager();
        var entity = await db.AssumptionDefinitions.SingleOrDefaultAsync(x => x.Id == definitionId && x.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Assumption definition was not found.");
        var old = new { entity.Name, entity.Unit, entity.Description, entity.IsActive };
        entity.Name = NormalizeRequired(request.Name, "Assumption name", 200);
        entity.Unit = NormalizeOptional(request.Unit, 50);
        entity.Description = NormalizeOptional(request.Description, 1000);
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("AssumptionDefinition", entity.Id, "UPDATE", new { entity.Code, entity.Name, entity.Unit, entity.Description, entity.IsActive }, old);
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<AssumptionValueDto>> GetValuesAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid? scenarioId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureCompanyRead(companyId);
        await EnsureFiscalYearAsync(companyId, fiscalYearId, requireOpen: false, cancellationToken);
        var query = db.AssumptionValues.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalYearId == fiscalYearId && x.Definition!.TenantId == user.TenantId);
        if (scenarioId.HasValue) query = query.Where(x => x.ScenarioId == scenarioId.Value || x.ScenarioId == null);
        return await query
            .OrderBy(x => x.Definition!.Name).ThenBy(x => x.ScenarioId).ThenBy(x => x.Period!.Sequence)
            .Select(x => new AssumptionValueDto(
                x.Id,
                x.DefinitionId,
                x.Definition!.Code,
                x.Definition.Name,
                x.Definition.Unit,
                x.CompanyId,
                x.FiscalYearId,
                x.ScenarioId,
                x.Scenario != null ? x.Scenario.Name : null,
                x.PeriodId,
                x.Period != null ? x.Period.Name : null,
                x.Value,
                x.Source,
                x.Note,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResolvedAssumptionDto>> ResolveAsync(
        Guid versionId,
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        var context = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new
            {
                x.ScenarioId,
                CompanyId = x.BudgetPlan!.CompanyId,
                FiscalYearId = x.BudgetPlan.FiscalYearId,
                TenantId = x.BudgetPlan.Company!.TenantId
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Budget version was not found.");
        if (context.TenantId != user.TenantId) throw new UnauthorizedAccessException("Budget version is outside the current tenant.");
        EnsureCompanyRead(context.CompanyId);
        if (!await db.FiscalPeriods.AnyAsync(x => x.Id == periodId && x.FiscalYearId == context.FiscalYearId, cancellationToken))
            throw new ArgumentException("Period does not belong to the budget version fiscal year.");

        var definitions = await db.AssumptionDefinitions.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var definitionIds = definitions.Select(x => x.Id).ToArray();
        var values = await db.AssumptionValues.AsNoTracking()
            .Where(x => definitionIds.Contains(x.DefinitionId)
                && x.CompanyId == context.CompanyId
                && x.FiscalYearId == context.FiscalYearId
                && (x.ScenarioId == null || x.ScenarioId == context.ScenarioId)
                && (x.PeriodId == null || x.PeriodId == periodId))
            .ToListAsync(cancellationToken);

        var result = new List<ResolvedAssumptionDto>();
        foreach (var definition in definitions)
        {
            var selected = SelectBest(values.Where(x => x.DefinitionId == definition.Id), context.ScenarioId, periodId);
            if (selected is null) continue;
            result.Add(new ResolvedAssumptionDto(
                definition.Id,
                definition.Code,
                $"ASSUMP:{definition.Code}",
                definition.Name,
                definition.Unit,
                selected.Value,
                selected.Id,
                selected.ScenarioId,
                selected.PeriodId,
                DescribeScope(selected, context.ScenarioId, periodId)));
        }
        return result;
    }

    public async Task<AssumptionSaveResultDto> UpsertValueAsync(
        UpsertAssumptionValueRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureCompanyWrite(request.CompanyId);
        await EnsureFiscalYearAsync(request.CompanyId, request.FiscalYearId, requireOpen: true, cancellationToken);
        var definition = await db.AssumptionDefinitions.SingleOrDefaultAsync(x => x.Id == request.DefinitionId && x.TenantId == user.TenantId && x.IsActive, cancellationToken)
            ?? throw new ArgumentException("Assumption definition is invalid or inactive.");
        await ValidateScopeAsync(request.CompanyId, request.FiscalYearId, request.ScenarioId, request.PeriodId, cancellationToken);

        AssumptionValue? entity = null;
        if (request.Id.HasValue)
            entity = await db.AssumptionValues.SingleOrDefaultAsync(x => x.Id == request.Id.Value && x.CompanyId == request.CompanyId, cancellationToken)
                ?? throw new KeyNotFoundException("Assumption value was not found.");
        else
            entity = await db.AssumptionValues.SingleOrDefaultAsync(x =>
                x.DefinitionId == request.DefinitionId
                && x.CompanyId == request.CompanyId
                && x.FiscalYearId == request.FiscalYearId
                && x.ScenarioId == request.ScenarioId
                && x.PeriodId == request.PeriodId, cancellationToken);

        var isNew = entity is null;
        var old = entity is null ? null : new
        {
            entity.DefinitionId,
            entity.FiscalYearId,
            entity.ScenarioId,
            entity.PeriodId,
            entity.Value,
            entity.Source,
            entity.Note
        };
        entity ??= new AssumptionValue { CompanyId = request.CompanyId, DefinitionId = request.DefinitionId, FiscalYearId = request.FiscalYearId };
        entity.DefinitionId = request.DefinitionId;
        entity.CompanyId = request.CompanyId;
        entity.FiscalYearId = request.FiscalYearId;
        entity.ScenarioId = request.ScenarioId;
        entity.PeriodId = request.PeriodId;
        entity.Value = request.Value;
        entity.Source = NormalizeOptional(request.Source, 200);
        entity.Note = NormalizeOptional(request.Note, 1000);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        if (isNew) db.AssumptionValues.Add(entity);

        AddAudit("AssumptionValue", entity.Id, isNew ? "CREATE" : "UPDATE", new
        {
            Definition = definition.Code,
            entity.CompanyId,
            entity.FiscalYearId,
            entity.ScenarioId,
            entity.PeriodId,
            entity.Value,
            entity.Source,
            entity.Note
        }, old);
        await db.SaveChangesAsync(cancellationToken);

        var recalculation = request.RecalculateDraftVersions
            ? await RecalculateAffectedDraftVersionsAsync(request.CompanyId, request.FiscalYearId, request.ScenarioId, cancellationToken)
            : RecalculationSummary.Empty;

        var dto = await GetValueDtoAsync(entity.Id, cancellationToken);
        return new AssumptionSaveResultDto(dto, recalculation.Versions, recalculation.Created, recalculation.Updated, recalculation.Skipped, recalculation.Errors);
    }

    public async Task DeleteValueAsync(
        Guid valueId,
        bool recalculateDraftVersions = true,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.AssumptionValues.Include(x => x.Definition).SingleOrDefaultAsync(x => x.Id == valueId, cancellationToken)
            ?? throw new KeyNotFoundException("Assumption value was not found.");
        if (entity.Definition?.TenantId != user.TenantId) throw new UnauthorizedAccessException("Assumption value is outside the current tenant.");
        EnsureCompanyWrite(entity.CompanyId);
        await EnsureFiscalYearAsync(entity.CompanyId, entity.FiscalYearId, requireOpen: true, cancellationToken);
        var scope = new { entity.CompanyId, entity.FiscalYearId, entity.ScenarioId, entity.PeriodId, entity.DefinitionId, entity.Value };
        db.AssumptionValues.Remove(entity);
        AddAudit("AssumptionValue", entity.Id, "DELETE", new { Deleted = true }, scope);
        await db.SaveChangesAsync(cancellationToken);
        if (recalculateDraftVersions)
            await RecalculateAffectedDraftVersionsAsync(entity.CompanyId, entity.FiscalYearId, entity.ScenarioId, cancellationToken);
    }

    private async Task<AssumptionValueDto> GetValueDtoAsync(Guid valueId, CancellationToken ct) =>
        await db.AssumptionValues.AsNoTracking().Where(x => x.Id == valueId)
            .Select(x => new AssumptionValueDto(
                x.Id,
                x.DefinitionId,
                x.Definition!.Code,
                x.Definition.Name,
                x.Definition.Unit,
                x.CompanyId,
                x.FiscalYearId,
                x.ScenarioId,
                x.Scenario != null ? x.Scenario.Name : null,
                x.PeriodId,
                x.Period != null ? x.Period.Name : null,
                x.Value,
                x.Source,
                x.Note,
                x.UpdatedAtUtc))
            .SingleAsync(ct);

    private async Task<RecalculationSummary> RecalculateAffectedDraftVersionsAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid? scenarioId,
        CancellationToken ct)
    {
        var query = db.BudgetVersions.AsNoTracking().Where(x =>
            x.BudgetPlan!.CompanyId == companyId
            && x.BudgetPlan.FiscalYearId == fiscalYearId
            && x.Status == BudgetStatus.Draft
            && !x.IsLocked);
        if (scenarioId.HasValue) query = query.Where(x => x.ScenarioId == scenarioId.Value);
        var versionIds = await query.Select(x => x.Id).ToListAsync(ct);
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();
        foreach (var versionId in versionIds)
        {
            ct.ThrowIfCancellationRequested();
            var result = await calculation.RecalculateVersionAsync(versionId, ct);
            created += result.FactsCreated;
            updated += result.FactsUpdated;
            skipped += result.FormulasSkipped;
            errors.AddRange(result.Errors);
        }
        return new RecalculationSummary(versionIds.Count, created, updated, skipped, errors.Distinct().Take(200).ToList());
    }

    private async Task ValidateScopeAsync(Guid companyId, Guid fiscalYearId, Guid? scenarioId, Guid? periodId, CancellationToken ct)
    {
        if (scenarioId.HasValue && !await db.BudgetScenarios.AnyAsync(x => x.Id == scenarioId.Value && x.TenantId == user.TenantId && x.IsActive, ct))
            throw new ArgumentException("Scenario is invalid or inactive.");
        if (periodId.HasValue)
        {
            var period = await db.FiscalPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId.Value && x.FiscalYearId == fiscalYearId, ct)
                ?? throw new ArgumentException("Period does not belong to the selected fiscal year.");
            if (period.IsClosed) throw new InvalidOperationException("Closed fiscal periods cannot accept assumption changes.");
        }
        if (!await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, ct))
            throw new ArgumentException("Company is invalid or inactive.");
    }

    private async Task EnsureFiscalYearAsync(Guid companyId, Guid fiscalYearId, bool requireOpen, CancellationToken ct)
    {
        var year = await db.FiscalYears.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, ct)
            ?? throw new ArgumentException("Fiscal year does not belong to the selected company.");
        if (requireOpen && year.IsClosed) throw new InvalidOperationException("Closed fiscal years cannot accept assumption changes.");
    }

    private void EnsureDefinitionManager()
    {
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN") && !user.IsInRole("CFO") && !user.IsInRole("BUDGET_MANAGER"))
            throw new UnauthorizedAccessException("Budget manager, CFO or administrator role is required to manage assumption definitions.");
    }

    private void EnsureCompanyRead(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private void EnsureCompanyWrite(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private void AddAudit(string entityType, Guid entityId, string action, object newValue, object? oldValue = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Action = action,
            OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValueJson = JsonSerializer.Serialize(newValue)
        });

    private static AssumptionDefinitionDto Map(AssumptionDefinition x) => new(x.Id, x.Code, x.Name, x.Unit, x.Description, x.IsActive);

    private static AssumptionValue? SelectBest(IEnumerable<AssumptionValue> candidates, Guid scenarioId, Guid periodId) =>
        candidates.OrderByDescending(x => x.ScenarioId == scenarioId ? 2 : 0)
            .ThenByDescending(x => x.PeriodId == periodId ? 1 : 0)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefault();

    private static string DescribeScope(AssumptionValue value, Guid scenarioId, Guid periodId) => (value.ScenarioId == scenarioId, value.PeriodId == periodId) switch
    {
        (true, true) => "ScenarioPeriod",
        (true, false) => "ScenarioAnnual",
        (false, true) => "GlobalPeriod",
        _ => "GlobalAnnual"
    };

    private static string NormalizeCode(string? value)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 64 || code.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')))
            throw new ArgumentException("Assumption code must contain 2-64 letters, numbers or underscore characters.");
        return code;
    }

    private static string NormalizeRequired(string? value, string label, int maxLength)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > maxLength) throw new ArgumentException($"{label} is required and must be at most {maxLength} characters.");
        return text;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > maxLength) throw new ArgumentException($"Text must be at most {maxLength} characters.");
        return text;
    }

    private sealed record RecalculationSummary(int Versions, int Created, int Updated, int Skipped, IReadOnlyList<string> Errors)
    {
        public static readonly RecalculationSummary Empty = new(0, 0, 0, 0, []);
    }
}
