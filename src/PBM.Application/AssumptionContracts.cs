namespace PBM.Application;

public sealed record AssumptionDefinitionDto(
    Guid Id,
    string Code,
    string Name,
    string? Unit,
    string? Description,
    bool IsActive);

public sealed record AssumptionValueDto(
    Guid Id,
    Guid DefinitionId,
    string DefinitionCode,
    string DefinitionName,
    string? Unit,
    Guid CompanyId,
    Guid FiscalYearId,
    Guid? ScenarioId,
    string? ScenarioName,
    Guid? PeriodId,
    string? PeriodName,
    decimal Value,
    string? Source,
    string? Note,
    DateTime UpdatedAtUtc);

public sealed record ResolvedAssumptionDto(
    Guid DefinitionId,
    string Code,
    string VariableName,
    string Name,
    string? Unit,
    decimal Value,
    Guid ValueId,
    Guid? ScenarioId,
    Guid? PeriodId,
    string ResolutionScope);

public sealed record CreateAssumptionDefinitionRequest(
    string Code,
    string Name,
    string? Unit,
    string? Description);

public sealed record UpdateAssumptionDefinitionRequest(
    string Name,
    string? Unit,
    string? Description,
    bool IsActive);

public sealed record UpsertAssumptionValueRequest(
    Guid? Id,
    Guid DefinitionId,
    Guid CompanyId,
    Guid FiscalYearId,
    Guid? ScenarioId,
    Guid? PeriodId,
    decimal Value,
    string? Source,
    string? Note,
    bool RecalculateDraftVersions = true);

public sealed record AssumptionSaveResultDto(
    AssumptionValueDto Value,
    int VersionsRecalculated,
    int FormulaFactsCreated,
    int FormulaFactsUpdated,
    int FormulasSkipped,
    IReadOnlyList<string> RecalculationErrors);

public interface IAssumptionService
{
    Task<IReadOnlyList<AssumptionDefinitionDto>> GetDefinitionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<AssumptionDefinitionDto> CreateDefinitionAsync(CreateAssumptionDefinitionRequest request, CancellationToken cancellationToken = default);
    Task<AssumptionDefinitionDto> UpdateDefinitionAsync(Guid definitionId, UpdateAssumptionDefinitionRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssumptionValueDto>> GetValuesAsync(Guid companyId, Guid fiscalYearId, Guid? scenarioId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResolvedAssumptionDto>> ResolveAsync(Guid versionId, Guid periodId, CancellationToken cancellationToken = default);
    Task<AssumptionSaveResultDto> UpsertValueAsync(UpsertAssumptionValueRequest request, CancellationToken cancellationToken = default);
    Task DeleteValueAsync(Guid valueId, bool recalculateDraftVersions = true, CancellationToken cancellationToken = default);
}
