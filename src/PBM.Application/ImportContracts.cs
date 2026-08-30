using PBM.Domain;

namespace PBM.Application;

public enum WorkbookTemplateProfile
{
    Unknown = 0,
    ReferenceData = 1,
    TradePlan = 2,
    ProductPrice = 3,
    MonthlyTrade = 4,
    HumanResources = 5,
    DepartmentExpense = 6,
    PurchaseDetail = 7,
    InventoryMovement = 8,
    Financing = 9,
    ProfitLoss = 10,
    BalanceSheet = 11,
    CashFlow = 12,
    ReceivablePayable = 13,
    Ratios = 14,
    Cover = 15
}

public sealed record WorkbookSheetPreviewDto(
    string Name,
    int RowCount,
    int ColumnCount,
    IReadOnlyList<IReadOnlyList<string?>> PreviewRows,
    WorkbookTemplateProfile SuggestedProfile,
    string? SuggestedModelCode,
    int ConfidencePercent,
    IReadOnlyList<string> Tags);

public sealed record WorkbookInspectionDto(string FileName, long FileSize, IReadOnlyList<WorkbookSheetPreviewDto> Sheets);

public sealed record NormalizedWorkbookFactDto(
    int SourceRow,
    string MeasureCode,
    ValueKind ValueKind,
    string? PeriodName,
    decimal Value,
    string Unit,
    decimal ScaleApplied,
    IReadOnlyDictionary<string, string> DimensionMembers,
    string? SourceLabel);

public sealed record WorkbookNormalizationDto(
    string SheetName,
    WorkbookTemplateProfile Profile,
    string? ModelCode,
    int SourceRows,
    IReadOnlyList<NormalizedWorkbookFactDto> Facts,
    IReadOnlyList<string> Warnings);

public sealed record WorkbookImportExecutionRequest(
    Guid CompanyId,
    Guid FiscalYearId,
    string SheetName,
    WorkbookTemplateProfile Profile,
    ValueKind? OverrideValueKind);

public sealed record WorkbookImportExecutionDto(
    string SheetName,
    string ModelCode,
    Guid BudgetPlanId,
    Guid VersionId,
    int ImportedFacts,
    int UpdatedFacts,
    int CreatedDimensionMembers,
    int SkippedFacts,
    IReadOnlyList<string> Warnings);

public interface IWorkbookImportService
{
    Task<WorkbookInspectionDto> InspectAsync(Stream stream, string fileName, long fileSize, int previewRows = 8, int previewColumns = 20, CancellationToken cancellationToken = default);
}

public interface IWorkbookNormalizationService
{
    Task<WorkbookNormalizationDto> NormalizeAsync(Stream stream, string sheetName, WorkbookTemplateProfile profile, CancellationToken cancellationToken = default);
}

public interface IWorkbookImportExecutionService
{
    Task<WorkbookImportExecutionDto> ImportAsync(Stream stream, string fileName, WorkbookImportExecutionRequest request, CancellationToken cancellationToken = default);
}
