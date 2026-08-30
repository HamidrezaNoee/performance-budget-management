using PBM.Domain;

namespace PBM.Application;

public sealed record PurchaseForecastMemberDto(
    Guid Id,
    Guid DimensionId,
    string Code,
    string Name);

public sealed record PurchaseForecastDimensionDto(
    Guid Id,
    string Code,
    string Name,
    int Sequence,
    bool IsRequired,
    IReadOnlyList<PurchaseForecastMemberDto> Members);

public sealed record PurchaseForecastMeasureDto(
    Guid Id,
    string Code,
    string Name,
    string? Unit,
    MeasureValueType ValueType,
    MeasureAggregation Aggregation);

public sealed record PurchaseForecastSetupDto(
    Guid ModelId,
    string ModelName,
    string BaseCurrencyCode,
    IReadOnlyList<PurchaseForecastDimensionDto> Dimensions,
    IReadOnlyList<PurchaseForecastMemberDto> CostTypes,
    PurchaseForecastMeasureDto QuantityMeasure,
    PurchaseForecastMeasureDto AmountMeasure,
    PurchaseForecastMeasureDto CostAmountMeasure,
    PurchaseForecastMeasureDto CostRateMeasure);

public sealed record CreatePurchaseCostTypeRequest(
    Guid CompanyId,
    string Code,
    string Name);

public sealed record PurchaseForecastQueryRequest(
    Guid VersionId,
    IReadOnlyList<DimensionSelection> Dimensions,
    ValueKind ValueKind = ValueKind.Forecast);

public sealed record PurchaseForecastPeriodValueDto(
    Guid PeriodId,
    string PeriodName,
    int Sequence,
    decimal Value,
    Guid? FactId = null);

public sealed record PurchaseForecastCostSeriesDto(
    Guid CostTypeId,
    string Code,
    string Name,
    IReadOnlyList<PurchaseForecastPeriodValueDto> Amounts,
    IReadOnlyList<PurchaseForecastPeriodValueDto> Rates);

public sealed record PurchaseForecastDataDto(
    IReadOnlyList<FiscalPeriodDto> Periods,
    IReadOnlyList<PurchaseForecastPeriodValueDto> Quantity,
    IReadOnlyList<PurchaseForecastPeriodValueDto> Amount,
    IReadOnlyList<PurchaseForecastCostSeriesDto> Costs);

public sealed record UpsertPurchaseForecastCellRequest(
    Guid VersionId,
    Guid PeriodId,
    string MeasureCode,
    decimal Value,
    IReadOnlyList<DimensionSelection> Dimensions,
    Guid? CostTypeId = null,
    string? Note = null,
    ValueKind ValueKind = ValueKind.Forecast);

public interface IPurchaseForecastService
{
    Task<PurchaseForecastSetupDto> GetSetupAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<PurchaseForecastMemberDto> CreateCostTypeAsync(CreatePurchaseCostTypeRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseForecastDataDto> QueryAsync(PurchaseForecastQueryRequest request, CancellationToken cancellationToken = default);
    Task<Guid> UpsertCellAsync(UpsertPurchaseForecastCellRequest request, CancellationToken cancellationToken = default);
}
