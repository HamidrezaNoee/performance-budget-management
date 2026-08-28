using PBM.Domain;

namespace PBM.Application;

public sealed record FormulaMeasureDto(
    Guid Id,
    Guid BudgetModelId,
    string Code,
    string Name,
    string? Unit,
    MeasureValueType ValueType,
    MeasureAggregation Aggregation,
    bool IsCalculated,
    string? FormulaExpression,
    int DisplayOrder);

public sealed record FormulaValidationDto(
    bool IsValid,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> MeasureDependencies,
    IReadOnlyList<string> AssumptionDependencies,
    IReadOnlyList<string> MissingDependencies,
    IReadOnlyList<string> Errors);

public sealed record ValidateFormulaRequest(
    Guid BudgetModelId,
    Guid? MeasureId,
    string Expression);

public sealed record UpdateMeasureFormulaRequest(
    string Expression,
    bool RecalculateDraftVersions = true);

public sealed record FormulaUpdateResultDto(
    FormulaMeasureDto Measure,
    FormulaValidationDto Validation,
    int VersionsRecalculated,
    int FactsCreated,
    int FactsUpdated,
    int FormulasSkipped,
    IReadOnlyList<string> RecalculationErrors);

public interface IFormulaAdminService
{
    Task<IReadOnlyList<FormulaMeasureDto>> GetMeasuresAsync(Guid budgetModelId, CancellationToken cancellationToken = default);
    Task<FormulaValidationDto> ValidateAsync(ValidateFormulaRequest request, CancellationToken cancellationToken = default);
    Task<FormulaUpdateResultDto> UpdateFormulaAsync(Guid measureId, UpdateMeasureFormulaRequest request, CancellationToken cancellationToken = default);
}
