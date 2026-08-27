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

public interface IWorkbookImportService
{
    Task<WorkbookInspectionDto> InspectAsync(Stream stream, string fileName, long fileSize, int previewRows = 8, int previewColumns = 20, CancellationToken cancellationToken = default);
}
