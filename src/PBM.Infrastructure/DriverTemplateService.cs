using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class DriverTemplateService(
    PbmDbContext db,
    IUserContext user,
    IFormulaAdminService formulaAdmin,
    ICalculationService calculation) : IDriverTemplateService
{
    public Task<IReadOnlyList<DriverTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DriverTemplateCatalog.GetAll());
    }

    public async Task<ApplyDriverTemplateResultDto> ApplyAsync(
        ApplyDriverTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManagerRole();
        var template = DriverTemplateCatalog.GetRequired(request.TemplateCode);
        var model = await db.BudgetModels
            .SingleOrDefaultAsync(x => x.Id == request.BudgetModelId && x.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget model was not found.");
        await EnsureCanChangeSharedModelAsync(model.Id, cancellationToken);

        var conflicts = new List<DriverTemplateConflictDto>();
        var assumptionsCreated = 0;
        var measuresCreated = 0;
        var measuresUpdated = 0;
        var measuresUnchanged = 0;
        var validationErrors = new List<string>();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var assumptions = await db.AssumptionDefinitions
            .Where(x => x.TenantId == user.TenantId)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        foreach (var spec in template.Assumptions)
        {
            if (assumptions.TryGetValue(spec.Code, out var existing))
            {
                if (!existing.IsActive)
                {
                    if (!request.OverwriteCompatibleDefinitions)
                    {
                        conflicts.Add(new("Assumption", spec.Code, "The required assumption exists but is inactive."));
                        continue;
                    }
                    existing.IsActive = true;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                }
                continue;
            }

            var definition = new AssumptionDefinition
            {
                TenantId = user.TenantId,
                Code = spec.Code,
                Name = spec.Name,
                Unit = spec.Unit,
                Description = spec.Description,
                IsActive = true
            };
            db.AssumptionDefinitions.Add(definition);
            assumptions[spec.Code] = definition;
            assumptionsCreated++;
        }

        var existingMeasures = await db.Measures
            .Where(x => x.BudgetModelId == model.Id)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var factUsage = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.Measure!.BudgetModelId == model.Id)
            .GroupBy(x => x.MeasureId)
            .Select(g => new
            {
                MeasureId = g.Key,
                AnyFacts = g.Any(),
                HasNonDraftFacts = g.Any(x => x.Version!.Status != BudgetStatus.Draft)
            })
            .ToDictionaryAsync(x => x.MeasureId, cancellationToken);

        foreach (var spec in template.Measures)
        {
            if (!existingMeasures.TryGetValue(spec.Code, out var measure))
            {
                measure = new MeasureDefinition
                {
                    BudgetModelId = model.Id,
                    Code = spec.Code,
                    Name = spec.Name,
                    Unit = spec.Unit,
                    ValueType = spec.ValueType,
                    Aggregation = spec.Aggregation,
                    IsCalculated = spec.IsCalculated,
                    FormulaExpression = spec.FormulaExpression,
                    DisplayOrder = spec.DisplayOrder
                };
                db.Measures.Add(measure);
                existingMeasures[spec.Code] = measure;
                measuresCreated++;
                continue;
            }

            var usage = factUsage.GetValueOrDefault(measure.Id);
            var hasFacts = usage?.AnyFacts ?? false;
            var hasNonDraftFacts = usage?.HasNonDraftFacts ?? false;
            var structuralMismatch = measure.ValueType != spec.ValueType || measure.Aggregation != spec.Aggregation;
            var calculationModeMismatch = measure.IsCalculated != spec.IsCalculated;
            var formulaMismatch = spec.IsCalculated
                && !string.Equals(measure.FormulaExpression?.Trim(), spec.FormulaExpression?.Trim(), StringComparison.OrdinalIgnoreCase);

            if (!structuralMismatch && !calculationModeMismatch && !formulaMismatch)
            {
                measuresUnchanged++;
                if (request.OverwriteCompatibleDefinitions)
                {
                    measure.Name = spec.Name;
                    measure.Unit = spec.Unit;
                    measure.DisplayOrder = spec.DisplayOrder;
                    measure.UpdatedAtUtc = DateTime.UtcNow;
                }
                continue;
            }

            if (!request.OverwriteCompatibleDefinitions)
            {
                conflicts.Add(new("Measure", spec.Code, BuildMismatchReason(structuralMismatch, calculationModeMismatch, formulaMismatch)));
                continue;
            }

            if ((structuralMismatch || calculationModeMismatch) && hasFacts)
            {
                conflicts.Add(new("Measure", spec.Code, "Value type, aggregation or manual/calculated mode cannot be changed because facts already exist."));
                continue;
            }
            if (formulaMismatch && hasNonDraftFacts)
            {
                conflicts.Add(new("Measure", spec.Code, "The calculated formula cannot be replaced because non-Draft facts already exist."));
                continue;
            }

            measure.Name = spec.Name;
            measure.Unit = spec.Unit;
            measure.ValueType = spec.ValueType;
            measure.Aggregation = spec.Aggregation;
            measure.IsCalculated = spec.IsCalculated;
            measure.FormulaExpression = spec.FormulaExpression;
            measure.DisplayOrder = spec.DisplayOrder;
            measure.UpdatedAtUtc = DateTime.UtcNow;
            measuresUpdated++;
        }

        if (conflicts.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmptyResult(template.Code, model.Id, conflicts);
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var spec in template.Measures.Where(x => x.IsCalculated && !string.IsNullOrWhiteSpace(x.FormulaExpression)))
        {
            var measure = existingMeasures[spec.Code];
            var validation = await formulaAdmin.ValidateAsync(
                new ValidateFormulaRequest(model.Id, measure.Id, spec.FormulaExpression!),
                cancellationToken);
            if (!validation.IsValid)
            {
                validationErrors.AddRange(validation.Errors.Select(x => $"{spec.Code}: {x}"));
                validationErrors.AddRange(validation.MissingDependencies.Select(x => $"{spec.Code}: missing dependency {x}"));
            }
        }

        if (validationErrors.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ApplyDriverTemplateResultDto(
                template.Code, model.Id, 0, 0, 0, 0, 0, 0, 0, 0,
                [], validationErrors.Distinct().ToList(), []);
        }

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "BudgetModel",
            EntityId = model.Id.ToString(),
            Action = "APPLY_DRIVER_TEMPLATE",
            NewValueJson = JsonSerializer.Serialize(new
            {
                TemplateCode = template.Code,
                model.Code,
                AssumptionsCreated = assumptionsCreated,
                MeasuresCreated = measuresCreated,
                MeasuresUpdated = measuresUpdated,
                MeasuresUnchanged = measuresUnchanged,
                request.OverwriteCompatibleDefinitions
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var recalc = request.RecalculateDraftVersions
            ? await RecalculateDraftVersionsAsync(model.Id, cancellationToken)
            : RecalculationSummary.Empty;

        return new ApplyDriverTemplateResultDto(
            template.Code,
            model.Id,
            assumptionsCreated,
            measuresCreated,
            measuresUpdated,
            measuresUnchanged,
            recalc.Versions,
            recalc.Created,
            recalc.Updated,
            recalc.Skipped,
            [],
            [],
            recalc.Errors);
    }

    private async Task<RecalculationSummary> RecalculateDraftVersionsAsync(Guid modelId, CancellationToken cancellationToken)
    {
        var versions = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.BudgetModelId == modelId && x.Status == BudgetStatus.Draft && !x.IsLocked)
            .Select(x => new { x.Id, x.BudgetPlan!.CompanyId })
            .ToListAsync(cancellationToken);
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN"))
            versions = versions.Where(x => user.CanWriteCompany(x.CompanyId)).ToList();

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();
        foreach (var version in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await calculation.RecalculateVersionAsync(version.Id, cancellationToken);
            created += result.FactsCreated;
            updated += result.FactsUpdated;
            skipped += result.FormulasSkipped;
            errors.AddRange(result.Errors);
        }

        return new RecalculationSummary(versions.Count, created, updated, skipped, errors.Distinct().Take(200).ToList());
    }

    private async Task EnsureCanChangeSharedModelAsync(Guid modelId, CancellationToken cancellationToken)
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN")) return;
        var companyIds = await db.BudgetPlans.AsNoTracking()
            .Where(x => x.BudgetModelId == modelId)
            .Select(x => x.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (companyIds.Any(x => !user.CanWriteCompany(x)))
            throw new UnauthorizedAccessException("This shared budget model is used by companies outside your write scope. A tenant administrator must apply the template.");
    }

    private void EnsureManagerRole()
    {
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN") && !user.IsInRole("CFO") && !user.IsInRole("BUDGET_MANAGER"))
            throw new UnauthorizedAccessException("Budget manager, CFO or administrator role is required to apply driver templates.");
    }

    private static string BuildMismatchReason(bool structural, bool mode, bool formula)
    {
        var parts = new List<string>();
        if (structural) parts.Add("value type or aggregation differs");
        if (mode) parts.Add("manual/calculated mode differs");
        if (formula) parts.Add("formula differs");
        return $"Existing measure conflicts with the template: {string.Join(", ", parts)}.";
    }

    private static ApplyDriverTemplateResultDto EmptyResult(
        string templateCode,
        Guid modelId,
        IReadOnlyList<DriverTemplateConflictDto> conflicts) =>
        new(templateCode, modelId, 0, 0, 0, 0, 0, 0, 0, 0, conflicts, [], []);

    private sealed record RecalculationSummary(int Versions, int Created, int Updated, int Skipped, IReadOnlyList<string> Errors)
    {
        public static readonly RecalculationSummary Empty = new(0, 0, 0, 0, []);
    }
}
