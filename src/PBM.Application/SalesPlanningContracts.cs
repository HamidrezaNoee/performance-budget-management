using PBM.Domain;

namespace PBM.Application;

public sealed record SalesPlanningMemberDto(Guid Id, Guid DimensionId, string Code, string Name);
public sealed record SalesPlanningDimensionDto(Guid Id, string Code, string Name, int Sequence, bool IsRequired, IReadOnlyList<SalesPlanningMemberDto> Members);
public sealed record SalesPlanningMeasureDto(Guid Id, string Code, string Name, string? Unit, MeasureValueType ValueType, bool IsCalculated);

public sealed record SalesPlanningSetupDto(
    Guid ModelId,
    string ModelName,
    string BaseCurrencyCode,
    IReadOnlyList<SalesPlanningDimensionDto> Dimensions,
    IReadOnlyList<SalesPlanningMeasureDto> Measures);

public sealed record SalesPlanningQueryRequest(
    Guid VersionId,
    IReadOnlyList<DimensionSelection> Dimensions,
    ValueKind ValueKind = ValueKind.Forecast);

public sealed record SalesPlanningPeriodValueDto(Guid PeriodId, string PeriodName, int Sequence, decimal Value, Guid? FactId = null);

public sealed record SalesPlanningSeriesDto(
    string MeasureCode,
    string Name,
    string? Unit,
    bool IsCalculated,
    IReadOnlyList<SalesPlanningPeriodValueDto> Values);

public sealed record SalesPlanningDataDto(
    IReadOnlyList<FiscalPeriodDto> Periods,
    IReadOnlyList<SalesPlanningSeriesDto> Series);

public sealed record UpsertSalesPlanningCellRequest(
    Guid VersionId,
    Guid PeriodId,
    string MeasureCode,
    decimal Value,
    IReadOnlyList<DimensionSelection> Dimensions,
    ValueKind ValueKind = ValueKind.Forecast,
    string? Note = null);

public interface ISalesPlanningService
{
    Task<SalesPlanningSetupDto> GetSetupAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<SalesPlanningDataDto> QueryAsync(SalesPlanningQueryRequest request, CancellationToken cancellationToken = default);
    Task<Guid> UpsertCellAsync(UpsertSalesPlanningCellRequest request, CancellationToken cancellationToken = default);
}
