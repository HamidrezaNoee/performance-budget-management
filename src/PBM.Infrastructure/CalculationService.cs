using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed partial class CalculationService(PbmDbContext db, IUserContext user, IFormulaEngine formulaEngine) : ICalculationService
{
    public async Task<CalculationResultDto> RecalculateCoordinateAsync(
        Guid versionId,
        Guid periodId,
        ValueKind valueKind,
        IReadOnlyList<DimensionSelection> dimensions,
        CancellationToken cancellationToken = default)
    {
        var context = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new
            {
                x.Id,
                x.ScenarioId,
                CompanyId = x.BudgetPlan!.CompanyId,
                FiscalYearId = x.BudgetPlan.FiscalYearId,
                ModelId = x.BudgetPlan.BudgetModelId,
                TenantId = x.BudgetPlan.Company!.TenantId
            })
            .SingleAsync(cancellationToken);
        EnsureCompanyWrite(context.CompanyId);

        var period = await db.FiscalPeriods.AsNoTracking().Include(x => x.FiscalYear)
            .SingleOrDefaultAsync(x => x.Id == periodId && x.FiscalYearId == context.FiscalYearId, cancellationToken)
            ?? throw new ArgumentException("Period does not belong to the budget version fiscal year.");
        if (period.IsClosed || period.FiscalYear!.IsClosed) throw new InvalidOperationException("Closed fiscal periods cannot be recalculated.");

        var measures = await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == context.ModelId).OrderBy(x => x.DisplayOrder).ToListAsync(cancellationToken);
        var calculated = measures.Where(x => x.IsCalculated && !string.IsNullOrWhiteSpace(x.FormulaExpression)).ToList();
        if (calculated.Count == 0) return new CalculationResultDto(1, 0, 0, 0, []);

        var hash = BudgetCoordinateKey.Create(dimensions);
        var facts = await db.BudgetFacts.Include(x => x.Dimensions)
            .Where(x => x.VersionId == versionId && x.PeriodId == periodId && x.ValueKind == valueKind && x.CoordinateHash == hash)
            .ToListAsync(cancellationToken);
        var calculatedIds = calculated.Select(x => x.Id).ToHashSet();
        var measureById = measures.ToDictionary(x => x.Id);
        var variables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in facts.Where(x => !calculatedIds.Contains(x.MeasureId)))
            if (measureById.TryGetValue(fact.MeasureId, out var definition)) variables[definition.Code] = fact.Value;

        await AddResolvedAssumptionVariablesAsync(
            variables,
            context.TenantId,
            context.CompanyId,
            context.FiscalYearId,
            context.ScenarioId,
            periodId,
            cancellationToken);

        var existingCalculated = facts.Where(x => calculatedIds.Contains(x.MeasureId)).ToDictionary(x => x.MeasureId);
        var pending = calculated.ToList();
        var errors = new List<string>();
        var created = 0;
        var updated = 0;
        var failed = 0;
        var progress = true;
        var currencyCodes = facts.Where(x => !string.IsNullOrWhiteSpace(x.CurrencyCode)).Select(x => x.CurrencyCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var coordinateCurrency = currencyCodes.Count == 1 ? currencyCodes[0] : null;
        var coordinatesJson = JsonSerializer.Serialize(dimensions.OrderBy(x => x.DimensionId));

        while (pending.Count > 0 && progress)
        {
            progress = false;
            foreach (var measure in pending.ToArray())
            {
                var expression = measure.FormulaExpression!;
                var dependencies = ExtractDependencies(expression);
                if (dependencies.Any(x => !variables.ContainsKey(x))) continue;

                decimal value;
                try { value = formulaEngine.Evaluate(expression, variables); }
                catch (Exception ex) when (ex is DivideByZeroException or FormatException or OverflowException)
                {
                    errors.Add($"{measure.Code}: {ex.Message}");
                    pending.Remove(measure);
                    failed++;
                    continue;
                }

                if (existingCalculated.TryGetValue(measure.Id, out var fact))
                {
                    if (fact.Value != value || fact.CoordinatesJson != coordinatesJson)
                    {
                        fact.Value = value;
                        fact.CoordinatesJson = coordinatesJson;
                        fact.Source = "Formula";
                        fact.Note = expression;
                        fact.CurrencyCode = measure.ValueType == MeasureValueType.Amount ? coordinateCurrency : null;
                        fact.UpdatedAtUtc = DateTime.UtcNow;
                        updated++;
                    }
                }
                else
                {
                    fact = new BudgetFact
                    {
                        VersionId = versionId,
                        PeriodId = periodId,
                        MeasureId = measure.Id,
                        ValueKind = valueKind,
                        Value = value,
                        CurrencyCode = measure.ValueType == MeasureValueType.Amount ? coordinateCurrency : null,
                        CoordinateHash = hash,
                        CoordinatesJson = coordinatesJson,
                        Source = "Formula",
                        Note = expression
                    };
                    foreach (var dimension in dimensions)
                        fact.Dimensions.Add(new BudgetFactDimension { BudgetFactId = fact.Id, DimensionId = dimension.DimensionId, MemberId = dimension.MemberId });
                    db.BudgetFacts.Add(fact);
                    existingCalculated[measure.Id] = fact;
                    created++;
                }

                variables[measure.Code] = value;
                pending.Remove(measure);
                progress = true;
            }
        }

        foreach (var unresolved in pending)
        {
            var missing = ExtractDependencies(unresolved.FormulaExpression!).Where(x => !variables.ContainsKey(x)).ToArray();
            errors.Add(missing.Length == 0
                ? $"{unresolved.Code}: formula dependency cycle detected."
                : $"{unresolved.Code}: missing dependencies {string.Join(", ", missing)}.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return new CalculationResultDto(1, created, updated, failed + pending.Count, errors);
    }

    public async Task<CalculationResultDto> RecalculateVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var context = await db.BudgetVersions.AsNoTracking().Where(x => x.Id == versionId)
            .Select(x => new { CompanyId = x.BudgetPlan!.CompanyId, ModelId = x.BudgetPlan.BudgetModelId })
            .SingleAsync(cancellationToken);
        EnsureCompanyWrite(context.CompanyId);

        var calculatedIds = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == context.ModelId && x.IsCalculated)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var sourceFacts = await db.BudgetFacts.AsNoTracking().Include(x => x.Dimensions)
            .Where(x => x.VersionId == versionId && !calculatedIds.Contains(x.MeasureId))
            .ToListAsync(cancellationToken);
        var coordinates = sourceFacts.GroupBy(x => new { x.PeriodId, x.ValueKind, x.CoordinateHash })
            .Select(g => new
            {
                g.Key.PeriodId,
                g.Key.ValueKind,
                Dimensions = g.First().Dimensions.OrderBy(x => x.DimensionId).Select(x => new DimensionSelection(x.DimensionId, x.MemberId)).ToList()
            })
            .ToList();

        var created = 0; var updated = 0; var skipped = 0;
        var errors = new List<string>();
        foreach (var coordinate in coordinates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await RecalculateCoordinateAsync(versionId, coordinate.PeriodId, coordinate.ValueKind, coordinate.Dimensions, cancellationToken);
                created += result.FactsCreated; updated += result.FactsUpdated; skipped += result.FormulasSkipped; errors.AddRange(result.Errors);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                errors.Add(ex.Message);
            }
        }
        return new CalculationResultDto(coordinates.Count, created, updated, skipped, errors.Distinct().Take(200).ToList());
    }

    private async Task AddResolvedAssumptionVariablesAsync(
        IDictionary<string, decimal> variables,
        Guid tenantId,
        Guid companyId,
        Guid fiscalYearId,
        Guid scenarioId,
        Guid periodId,
        CancellationToken ct)
    {
        var definitions = await db.AssumptionDefinitions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .Select(x => new { x.Id, x.Code })
            .ToListAsync(ct);
        if (definitions.Count == 0) return;

        var definitionIds = definitions.Select(x => x.Id).ToArray();
        var values = await db.AssumptionValues.AsNoTracking()
            .Where(x => definitionIds.Contains(x.DefinitionId)
                && x.CompanyId == companyId
                && x.FiscalYearId == fiscalYearId
                && (x.ScenarioId == null || x.ScenarioId == scenarioId)
                && (x.PeriodId == null || x.PeriodId == periodId))
            .ToListAsync(ct);

        foreach (var definition in definitions)
        {
            var selected = values.Where(x => x.DefinitionId == definition.Id)
                .OrderByDescending(x => x.ScenarioId == scenarioId ? 2 : 0)
                .ThenByDescending(x => x.PeriodId == periodId ? 1 : 0)
                .ThenByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefault();
            if (selected is not null)
                variables[$"ASSUMP:{definition.Code}"] = selected.Value;
        }
    }

    private void EnsureCompanyWrite(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private static IReadOnlyList<string> ExtractDependencies(string expression) =>
        VariableRegex().Matches(expression).Cast<Match>().Select(x => x.Groups[1].Value.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    [GeneratedRegex(@"\[([^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();
}
