using PBM.Domain;

namespace PBM.Application;

public sealed record CalculationResultDto(
    int CoordinatesProcessed,
    int FactsCreated,
    int FactsUpdated,
    int FormulasSkipped,
    IReadOnlyList<string> Errors);

public interface ICalculationService
{
    Task<CalculationResultDto> RecalculateCoordinateAsync(
        Guid versionId,
        Guid periodId,
        ValueKind valueKind,
        IReadOnlyList<DimensionSelection> dimensions,
        CancellationToken cancellationToken = default);

    Task<CalculationResultDto> RecalculateVersionAsync(Guid versionId, CancellationToken cancellationToken = default);
}
