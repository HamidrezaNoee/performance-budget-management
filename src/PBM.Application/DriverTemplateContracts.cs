using PBM.Domain;

namespace PBM.Application;

public sealed record DriverTemplateAssumptionDto(
    string Code,
    string Name,
    string? Unit,
    string Description);

public sealed record DriverTemplateMeasureDto(
    string Code,
    string Name,
    string? Unit,
    MeasureValueType ValueType,
    MeasureAggregation Aggregation,
    bool IsCalculated,
    string? FormulaExpression,
    int DisplayOrder);

public sealed record DriverTemplateDto(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<string> RecommendedModelCodes,
    IReadOnlyList<DriverTemplateAssumptionDto> Assumptions,
    IReadOnlyList<DriverTemplateMeasureDto> Measures);

public sealed record ApplyDriverTemplateRequest(
    Guid BudgetModelId,
    string TemplateCode,
    bool OverwriteCompatibleDefinitions = false,
    bool RecalculateDraftVersions = false);

public sealed record DriverTemplateConflictDto(
    string EntityType,
    string Code,
    string Reason);

public sealed record ApplyDriverTemplateResultDto(
    string TemplateCode,
    Guid BudgetModelId,
    int AssumptionsCreated,
    int MeasuresCreated,
    int MeasuresUpdated,
    int MeasuresUnchanged,
    int VersionsRecalculated,
    int FactsCreated,
    int FactsUpdated,
    int FormulasSkipped,
    IReadOnlyList<DriverTemplateConflictDto> Conflicts,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> RecalculationErrors);

public interface IDriverTemplateService
{
    Task<IReadOnlyList<DriverTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    Task<ApplyDriverTemplateResultDto> ApplyAsync(ApplyDriverTemplateRequest request, CancellationToken cancellationToken = default);
}
