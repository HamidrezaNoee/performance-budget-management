using PBM.Domain;

namespace PBM.Application;

public enum BudgetSpreadMethod
{
    Even = 0,
    Weighted = 1
}

public sealed record CopyPriorYearActualRequest(
    Guid TargetVersionId,
    decimal GrowthPercent = 0,
    bool ReplaceExisting = false);

public sealed record BudgetBulkOperationResultDto(
    int Created,
    int Updated,
    int Skipped,
    int RecalculatedCoordinates,
    IReadOnlyList<string> Warnings);

public sealed record SpreadBudgetRequest(
    Guid VersionId,
    Guid MeasureId,
    ValueKind ValueKind,
    Guid RowDimensionId,
    Guid RowMemberId,
    IReadOnlyList<DimensionSelection> Filters,
    decimal Total,
    BudgetSpreadMethod Method = BudgetSpreadMethod.Even,
    IReadOnlyList<decimal>? Weights = null,
    string? CurrencyCode = null,
    string? Note = null);

public sealed record BulkBudgetCellInput(Guid PeriodId, decimal Value);
public sealed record BulkBudgetRowInput(Guid RowMemberId, IReadOnlyList<BulkBudgetCellInput> Cells);

public sealed record BulkBudgetPasteRequest(
    Guid VersionId,
    Guid MeasureId,
    ValueKind ValueKind,
    Guid RowDimensionId,
    IReadOnlyList<DimensionSelection> Filters,
    IReadOnlyList<BulkBudgetRowInput> Rows,
    string? CurrencyCode = null,
    string? Note = null);

public sealed record BudgetVersionComparisonQuery(
    Guid LeftVersionId,
    Guid RightVersionId,
    Guid MeasureId,
    ValueKind ValueKind,
    Guid RowDimensionId,
    IReadOnlyList<DimensionSelection> Filters);

public sealed record BudgetVersionComparisonCellDto(
    Guid PeriodId,
    decimal LeftValue,
    decimal RightValue,
    decimal Variance,
    decimal? VariancePercent);

public sealed record BudgetVersionComparisonRowDto(
    Guid MemberId,
    string Code,
    string Name,
    IReadOnlyList<BudgetVersionComparisonCellDto> Cells);

public sealed record BudgetVersionComparisonDto(
    Guid LeftVersionId,
    Guid RightVersionId,
    IReadOnlyList<FiscalPeriodDto> Periods,
    MeasureDto Measure,
    DimensionDto RowDimension,
    IReadOnlyList<BudgetVersionComparisonRowDto> Rows);

public interface IBudgetOperationsService
{
    Task<BudgetBulkOperationResultDto> CopyPriorYearActualAsync(
        CopyPriorYearActualRequest request,
        CancellationToken cancellationToken = default);

    Task<BudgetBulkOperationResultDto> SpreadAsync(
        SpreadBudgetRequest request,
        CancellationToken cancellationToken = default);

    Task<BudgetBulkOperationResultDto> BulkPasteAsync(
        BulkBudgetPasteRequest request,
        CancellationToken cancellationToken = default);

    Task<BudgetVersionComparisonDto> CompareVersionsAsync(
        BudgetVersionComparisonQuery query,
        CancellationToken cancellationToken = default);
}
