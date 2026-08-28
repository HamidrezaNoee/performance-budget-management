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
    public async Task<IReadOnlyList<FormulaMeasureDto>> GetMeasuresAsync(
        Guid budgetModelId,
        CancellationToken cancellationToken = default)
    {
        await EnsureModelTenantAsync(budgetModelId, cancellationToken);
        return await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == budgetModelId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => MapProjection(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<FormulaValidationDto> ValidateAsync(
        ValidateFormulaRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureModelTenantAsync(request.BudgetModelId, cancellationToken);
        return await ValidateInternalAsync(request.BudgetModelId, request.MeasureId, request.Expression, cancellationToken);
    }

    public async Task<FormulaUpdateResultDto> UpdateFormulaAsync(
        Guid measureId,
        UpdateMeasureFormulaRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManager();
        var measure = await db.Measures.Include(x => x.BudgetModel)
            .SingleOrDefaultAsync(x => x.Id == measureId && x.BudgetModel!.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Measure was not found.");
        var expression = (request.Expression ?? string.Empty).Trim();
        if (expression.Length is < 3 or > 4000)
            throw new ArgumentException("Formula expression must contain between 3 and 4000 characters.");

        var validation = await ValidateInternalAsync(measure.BudgetModelId, measure.Id, expression, cancellationToken);
        if (!validation.IsValid)
            throw new ArgumentException($"Formula is invalid: {string.Join(" | ", validation.Errors.Concat(validation.MissingDependencies.Select(x => $"Missing dependency: {x}")))}");

        var old = new { measure.IsCalculated, measure.FormulaExpression };
        measure.IsCalculated = true;
        measure.FormulaExpression = expression;
        measure.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("MeasureDefinition", measure.Id, "FORMULA_UPDATE", old, new
        {
            measure.Code,
            measure.IsCalculated,
            measure.FormulaExpression,
            validation.Dependencies
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
        EnsureManager();
        var measure = await db.Measures.Include(x => x.BudgetModel)
            .SingleOrDefaultAsync(x => x.Id == measureId && x.BudgetModel!.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Measure was not found.");
        var usedCalculatedFacts = await db.BudgetFacts.AnyAsync(x => x.MeasureId == measureId && x.Version!.Status != BudgetStatus.Draft, cancellationToken);
        if (usedCalculatedFacts)
            throw new InvalidOperationException("This formula has calculated facts in non-draft budget versions. Create a new measure/model version instead of converting it to a manual measure.");

        var old = new { measure.IsCalculated, measure.FormulaExpression };
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

        var measures = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == modelId)
            .ToListAsync(ct);
        var measureCodes = measures.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assumptionCodes = await db.AssumptionDefinitions.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsActive)
            .Select(x => x.Code)
            .ToListAsync(ct);
        var assumptionVariables = assumptionCodes.Select(x => $"ASSUMP:{x}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dependencies = ExtractDependencies(expression);
        var measureDependencies = dependencies.Where(measureCodes.Contains).OrderBy(x => x).ToList();
        var assumptionDependencies = dependencies.Where(assumptionVariables.Contains).OrderBy(x => x).ToList();
        var missing = dependencies
            .Where(x => !measureCodes.Contains(x) && !assumptionVariables.Contains(x))
            .OrderBy(x => x)
            .ToList();

        var target = targetMeasureId.HasValue ? measures.SingleOrDefault(x => x.Id == targetMeasureId.Value) : null;
        if (target is not null && measureDependencies.Contains(target.Code, StringComparer.OrdinalIgnoreCase))
            errors.Add("A calculated measure cannot reference itself.");

        if (errors.Count == 0 && missing.Count == 0)
        {
            var variables = dependencies.ToDictionary(x => x, _ => 1m, StringComparer.OrdinalIgnoreCase);
            try { _ = engine.Evaluate(expression, variables); }
            catch (DivideByZeroException) { /* Syntax is valid; dummy values happened to produce zero. */ }
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
            dependencies,
            measureDependencies,
            assumptionDependencies,
            missing,
            errors);
    }

    private async Task<RecalculationSummary> RecalculateDraftVersionsAsync(Guid modelId, CancellationToken ct)
    {
        var versionIds = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.BudgetModelId == modelId && x.Status == BudgetStatus.Draft && !x.IsLocked)
            .Select(x => new { x.Id, x.BudgetPlan!.CompanyId })
            .ToListAsync(ct);
        versionIds = versionIds.Where(x => user.IsInRole("SUPERADMIN") || user.CanWriteCompany(x.CompanyId)).ToList();

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();
        foreach (var version in versionIds)
        {
            ct.ThrowIfCancellationRequested();
            var result = await calculation.RecalculateVersionAsync(version.Id, ct);
            created += result.FactsCreated;
            updated += result.FactsUpdated;
            skipped += result.FormulasSkipped;
            errors.AddRange(result.Errors);
        }
        return new RecalculationSummary(versionIds.Count, created, updated, skipped, errors.Distinct().Take(200).ToList());
    }

    private async Task EnsureModelTenantAsync(Guid modelId, CancellationToken ct)
    {
        var tenantId = await db.BudgetModels.AsNoTracking().Where(x => x.Id == modelId).Select(x => (Guid?)x.TenantId).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Budget model was not found.");
        if (tenantId != user.TenantId) throw new UnauthorizedAccessException("Budget model is outside the current tenant.");
    }

    private void EnsureManager()
    {
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN") && !user.IsInRole("CFO") && !user.IsInRole("BUDGET_MANAGER"))
            throw new UnauthorizedAccessException("Budget manager, CFO or administrator role is required to manage formulas.");
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

    private static FormulaMeasureDto MapProjection(MeasureDefinition x) => new(
        x.Id, x.BudgetModelId, x.Code, x.Name, x.Unit, x.ValueType, x.Aggregation,
        x.IsCalculated, x.FormulaExpression, x.DisplayOrder);

    private static IReadOnlyList<string> ExtractDependencies(string expression) =>
        VariableRegex().Matches(expression ?? string.Empty).Cast<Match>()
            .Select(x => x.Groups[1].Value.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> FindCycle(
        IReadOnlyDictionary<string, string> formulas,
        IReadOnlySet<string> measureCodes)
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
