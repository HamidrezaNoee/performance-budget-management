using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed partial class FormulaAdminService(
    PbmDbContext db,
    IUserContext user,
    IFormulaEngine engine,
    ICalculationService calculation) : IFormulaAdminService
{
    public async Task<IReadOnlyList<FormulaMeasureDto>> GetMeasuresAsync(Guid budgetModelId, CancellationToken cancellationToken = default)
    {
        await EnsureModelTenantAsync(budgetModelId, cancellationToken);
        return await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == budgetModelId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new FormulaMeasureDto(
                x.Id, x.BudgetModelId, x.Code, x.Name, x.Unit, x.ValueType, x.Aggregation,
                x.IsCalculated, x.FormulaExpression, x.DisplayOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<FormulaMeasureDto> CreateMeasureAsync(CreateMeasureDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureManagerRole();
        await EnsureModelTenantAsync(request.BudgetModelId, cancellationToken);
        await EnsureCanChangeSharedModelAsync(request.BudgetModelId, cancellationToken);
        var code = NormalizeCode(request.Code);
        var name = NormalizeRequired(request.Name, "Measure name", 200);
        var unit = NormalizeOptional(request.Unit, 50);
        ValidateDisplayOrder(request.DisplayOrder);
        if (await db.Measures.AnyAsync(x => x.BudgetModelId == request.BudgetModelId && x.Code == code, cancellationToken))
            throw new InvalidOperationException("A measure with this code already exists in the selected budget model.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var measure = new MeasureDefinition
        {
            BudgetModelId = request.BudgetModelId,
            Code = code,
            Name = name,
            Unit = unit,
            ValueType = request.ValueType,
            Aggregation = request.Aggregation,
            DisplayOrder = request.DisplayOrder,
            IsCalculated = false,
            FormulaExpression = null
        };
        db.Measures.Add(measure);
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.FormulaExpression))
        {
            var expression = request.FormulaExpression.Trim();
            var validation = await ValidateInternalAsync(request.BudgetModelId, measure.Id, expression, cancellationToken);
            if (!validation.IsValid)
            {
                var messages = validation.Errors.Concat(validation.MissingDependencies.Select(x => $"Missing dependency: {x}"));
                throw new ArgumentException($"Formula is invalid: {string.Join(" | ", messages)}");
            }
            measure.IsCalculated = true;
            measure.FormulaExpression = expression;
        }

        AddAudit("MeasureDefinition", measure.Id, "CREATE", null, new
        {
            measure.BudgetModelId, measure.Code, measure.Name, measure.Unit, measure.ValueType,
            measure.Aggregation, measure.DisplayOrder, measure.IsCalculated, measure.FormulaExpression
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(measure);
    }

    public async Task<FormulaMeasureDto> UpdateMeasureAsync(
        Guid measureId,
        UpdateMeasureDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        var measure = await GetManagedMeasureAsync(measureId, cancellationToken);
        await EnsureCanChangeSharedModelAsync(measure.BudgetModelId, cancellationToken);
        var name = NormalizeRequired(request.Name, "Measure name", 200);
        var unit = NormalizeOptional(request.Unit, 50);
        ValidateDisplayOrder(request.DisplayOrder);

        var hasFacts = await db.BudgetFacts.AnyAsync(x => x.MeasureId == measureId, cancellationToken);
        if (hasFacts && (measure.ValueType != request.ValueType || measure.Aggregation != request.Aggregation))
            throw new InvalidOperationException("Value type and aggregation cannot be changed after facts exist for a measure. Create a new measure instead.");

        var old = new { measure.Name, measure.Unit, measure.ValueType, measure.Aggregation, measure.DisplayOrder };
        measure.Name = name;
        measure.Unit = unit;
        measure.ValueType = request.ValueType;
        measure.Aggregation = request.Aggregation;
        measure.DisplayOrder = request.DisplayOrder;
        measure.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("MeasureDefinition", measure.Id, "UPDATE", old, new
        {
            measure.Code, measure.Name, measure.Unit, measure.ValueType, measure.Aggregation, measure.DisplayOrder
        });
        await db.SaveChangesAsync(cancellationToken);
        return Map(measure);
    }

    public async Task DeleteMeasureAsync(Guid measureId, CancellationToken cancellationToken = default)
    {
        var measure = await GetManagedMeasureAsync(measureId, cancellationToken);
        await EnsureCanChangeSharedModelAsync(measure.BudgetModelId, cancellationToken);

        if (await db.BudgetFacts.AnyAsync(x => x.MeasureId == measureId, cancellationToken)
            || await db.BudgetReservations.AnyAsync(x => x.MeasureId == measureId, cancellationToken)
            || await db.BudgetTransfers.AnyAsync(x => x.MeasureId == measureId, cancellationToken))
            throw new InvalidOperationException("Measure is already used by budget facts, reservations or transfers and cannot be deleted.");

        var formulas = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == measure.BudgetModelId && x.Id != measure.Id && x.IsCalculated && x.FormulaExpression != null)
            .Select(x => new { x.Code, x.FormulaExpression })
            .ToListAsync(cancellationToken);
        var dependents = formulas.Where(x => ExtractDependencies(x.FormulaExpression!).Contains(measure.Code, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.Code).ToArray();
        if (dependents.Length > 0)
            throw new InvalidOperationException($"Measure is referenced by calculated measures: {string.Join(", ", dependents)}.");

        AddAudit("MeasureDefinition", measure.Id, "DELETE", new
        {
            measure.Code, measure.Name, measure.Unit, measure.ValueType, measure.Aggregation,
            measure.IsCalculated, measure.FormulaExpression
        }, new { Deleted = true });
        db.Measures.Remove(measure);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<FormulaValidationDto> ValidateAsync(ValidateFormulaRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureModelTenantAsync(request.BudgetModelId, cancellationToken);
        return await ValidateInternalAsync(request.BudgetModelId, request.MeasureId, request.Expression, cancellationToken);
    }

    public async Task<FormulaUpdateResultDto> UpdateFormulaAsync(
        Guid measureId,
        UpdateMeasureFormulaRequest request,
        CancellationToken cancellationToken = default)
    {
        var measure = await GetManagedMeasureAsync(measureId, cancellationToken);
        await EnsureCanChangeSharedModelAsync(measure.BudgetModelId, cancellationToken);
        var expression = (request.Expression ?? string.Empty).Trim();
        if (expression.Length is < 3 or > 4000)
            throw new ArgumentException("Formula expression must contain between 3 and 4000 characters.");

        var validation = await ValidateInternalAsync(measure.BudgetModelId, measure.Id, expression, cancellationToken);
        if (!validation.IsValid)
        {
            var messages = validation.Errors.Concat(validation.MissingDependencies.Select(x => $"Missing dependency: {x}"));
            throw new ArgumentException($"Formula is invalid: {string.Join(" | ", messages)}");
        }

        var old = new { measure.IsCalculated, measure.FormulaExpression };
        measure.IsCalculated = true;
        measure.FormulaExpression = expression;
        measure.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("MeasureDefinition", measure.Id, "FORMULA_UPDATE", old, new
        {
            measure.Code, measure.IsCalculated, measure.FormulaExpression, validation.Dependencies
        });
        await db.SaveChangesAsync(cancellationToken);

        var recalc = request.RecalculateDraftVersions
            ? await RecalculateDraftVersionsAsync(measure.BudgetModelId, cancellationToken)
            : RecalculationSummary.Empty;
        return new FormulaUpdateResultDto(
            Map(measure), validation, recalc.Versions, recalc.Created, recalc.Updated, recalc.Skipped, recalc.Errors);
    }

    public async Task<FormulaMeasureDto> ClearFormulaAsync(Guid measureId, CancellationToken cancellationToken = default)
    {
        var measure = await GetManagedMeasureAsync(measureId, cancellationToken);
        await EnsureCanChangeSharedModelAsync(measure.BudgetModelId, cancellationToken);

        var usedOutsideDraft = await db.BudgetFacts.AnyAsync(
            x => x.MeasureId == measureId && x.Version!.Status != BudgetStatus.Draft,
            cancellationToken);
        if (usedOutsideDraft)
            throw new InvalidOperationException("This formula has calculated facts in non-draft budget versions. Create a new measure/model version instead of converting it to a manual measure.");

        var draftFormulaFacts = await db.BudgetFacts
            .Where(x => x.MeasureId == measureId && x.Version!.Status == BudgetStatus.Draft && x.Source == "Formula")
            .ToListAsync(cancellationToken);
        if (draftFormulaFacts.Count > 0) db.BudgetFacts.RemoveRange(draftFormulaFacts);

        var old = new { measure.IsCalculated, measure.FormulaExpression, RemovedDraftFormulaFacts = draftFormulaFacts.Count };
        measure.IsCalculated = false;
        measure.FormulaExpression = null;
        measure.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("MeasureDefinition", measure.Id, "FORMULA_CLEAR", old, new { measure.Code, measure.IsCalculated });
        await db.SaveChangesAsync(cancellationToken);
        return Map(measure);
    }

    private async Task<FormulaValidationDto> ValidateInternalAsync(
        Guid modelId,
        Guid? targetMeasureId,
        string? rawExpression,
        CancellationToken ct)
    {
        var expression = (rawExpression ?? string.Empty).Trim();
        var errors = new List<string>();
        if (expression.Length is < 3 or > 4000)
            errors.Add("Formula expression must contain between 3 and 4000 characters.");

        var measures = await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == modelId).ToListAsync(ct);
        var measureCodes = measures.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assumptionCodes = await db.AssumptionDefinitions.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsActive)
            .Select(x => x.Code)
            .ToListAsync(ct);
        var assumptionVariables = assumptionCodes.Select(x => $"ASSUMP:{x}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dependencies = ExtractDependencies(expression);
        var measureDependencies = dependencies.Where(measureCodes.Contains).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var assumptionDependencies = dependencies.Where(assumptionVariables.Contains).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var missing = dependencies.Where(x => !measureCodes.Contains(x) && !assumptionVariables.Contains(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        var target = targetMeasureId.HasValue ? measures.SingleOrDefault(x => x.Id == targetMeasureId.Value) : null;
        if (targetMeasureId.HasValue && target is null) errors.Add("Target measure does not belong to the selected budget model.");
        if (target is not null && measureDependencies.Contains(target.Code, StringComparer.OrdinalIgnoreCase))
            errors.Add("A calculated measure cannot reference itself.");

        if (errors.Count == 0 && missing.Count == 0)
        {
            var variables = dependencies.ToDictionary(x => x, _ => 1m, StringComparer.OrdinalIgnoreCase);
            try { _ = engine.Evaluate(expression, variables); }
            catch (DivideByZeroException) { }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or KeyNotFoundException)
            {
                errors.Add(ex.Message);
            }
        }

        if (target is not null && errors.Count == 0 && missing.Count == 0)
        {
            var formulas = measures.Where(x => x.IsCalculated && !string.IsNullOrWhiteSpace(x.FormulaExpression))
                .ToDictionary(x => x.Code, x => x.FormulaExpression!, StringComparer.OrdinalIgnoreCase);
            formulas[target.Code] = expression;
            var cycle = FindCycle(formulas, measureCodes);
            if (cycle.Count > 0) errors.Add($"Formula dependency cycle detected: {string.Join(" -> ", cycle)}");
        }

        return new FormulaValidationDto(
            errors.Count == 0 && missing.Count == 0,
            dependencies, measureDependencies, assumptionDependencies, missing, errors);
    }

    private async Task<MeasureDefinition> GetManagedMeasureAsync(Guid measureId, CancellationToken ct)
    {
        EnsureManagerRole();
        return await db.Measures.Include(x => x.BudgetModel)
            .SingleOrDefaultAsync(x => x.Id == measureId && x.BudgetModel!.TenantId == user.TenantId, ct)
            ?? throw new KeyNotFoundException("Measure was not found.");
    }

    private async Task EnsureCanChangeSharedModelAsync(Guid modelId, CancellationToken ct)
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN")) return;
        var companyIds = await db.BudgetPlans.AsNoTracking().Where(x => x.BudgetModelId == modelId)
            .Select(x => x.CompanyId).Distinct().ToListAsync(ct);
        if (companyIds.Any(x => !user.CanWriteCompany(x)))
            throw new UnauthorizedAccessException("This is a shared budget model used by companies outside your write scope. A tenant administrator must change its definition.");
    }

    private async Task<RecalculationSummary> RecalculateDraftVersionsAsync(Guid modelId, CancellationToken ct)
    {
        var versions = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.BudgetModelId == modelId && x.Status == BudgetStatus.Draft && !x.IsLocked)
            .Select(x => new { x.Id, x.BudgetPlan!.CompanyId })
            .ToListAsync(ct);
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN"))
            versions = versions.Where(x => user.CanWriteCompany(x.CompanyId)).ToList();

        var created = 0; var updated = 0; var skipped = 0; var errors = new List<string>();
        foreach (var version in versions)
        {
            ct.ThrowIfCancellationRequested();
            var result = await calculation.RecalculateVersionAsync(version.Id, ct);
            created += result.FactsCreated; updated += result.FactsUpdated; skipped += result.FormulasSkipped; errors.AddRange(result.Errors);
        }
        return new RecalculationSummary(versions.Count, created, updated, skipped, errors.Distinct().Take(200).ToList());
    }

    private async Task EnsureModelTenantAsync(Guid modelId, CancellationToken ct)
    {
        var tenantId = await db.BudgetModels.AsNoTracking().Where(x => x.Id == modelId)
            .Select(x => (Guid?)x.TenantId).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Budget model was not found.");
        if (tenantId != user.TenantId) throw new UnauthorizedAccessException("Budget model is outside the current tenant.");
    }

    private void EnsureManagerRole()
    {
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN") && !user.IsInRole("CFO") && !user.IsInRole("BUDGET_MANAGER"))
            throw new UnauthorizedAccessException("Budget manager, CFO or administrator role is required to manage measures and formulas.");
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

    private static FormulaMeasureDto Map(MeasureDefinition x) => new(
        x.Id, x.BudgetModelId, x.Code, x.Name, x.Unit, x.ValueType, x.Aggregation,
        x.IsCalculated, x.FormulaExpression, x.DisplayOrder);

    private static string NormalizeCode(string? value)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 64 || code.Any(ch => !(ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9' || ch == '_')))
            throw new ArgumentException("Measure code must contain 2-64 ASCII uppercase letters, numbers or underscore characters.");
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

    private static void ValidateDisplayOrder(int value)
    {
        if (value is < -10000 or > 10000) throw new ArgumentException("Display order must be between -10000 and 10000.");
    }

    private static IReadOnlyList<string> ExtractDependencies(string expression) =>
        VariableRegex().Matches(expression ?? string.Empty).Cast<Match>()
            .Select(x => x.Groups[1].Value.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

    private static IReadOnlyList<string> FindCycle(IReadOnlyDictionary<string, string> formulas, IReadOnlySet<string> measureCodes)
    {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();
        foreach (var code in formulas.Keys)
        {
            var cycle = Visit(code);
            if (cycle.Count > 0) return cycle;
        }
        return [];

        IReadOnlyList<string> Visit(string code)
        {
            if (state.TryGetValue(code, out var existing))
            {
                if (existing != 1) return [];
                var start = path.FindIndex(x => string.Equals(x, code, StringComparison.OrdinalIgnoreCase));
                return start >= 0 ? path.Skip(start).Append(code).ToList() : [code, code];
            }
            state[code] = 1;
            path.Add(code);
            if (formulas.TryGetValue(code, out var expression))
            {
                foreach (var dependency in ExtractDependencies(expression).Where(measureCodes.Contains))
                {
                    if (!formulas.ContainsKey(dependency)) continue;
                    var cycle = Visit(dependency);
                    if (cycle.Count > 0) return cycle;
                }
            }
            path.RemoveAt(path.Count - 1);
            state[code] = 2;
            return [];
        }
    }

    [GeneratedRegex(@"\[([^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();

    private sealed record RecalculationSummary(int Versions, int Created, int Updated, int Skipped, IReadOnlyList<string> Errors)
    {
        public static readonly RecalculationSummary Empty = new(0, 0, 0, 0, []);
    }
}
