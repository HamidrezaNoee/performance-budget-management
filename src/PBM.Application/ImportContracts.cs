namespace PBM.Application;

public sealed record WorkbookSheetPreviewDto(string Name, int RowCount, int ColumnCount, IReadOnlyList<IReadOnlyList<string?>> PreviewRows);
public sealed record WorkbookInspectionDto(string FileName, long FileSize, IReadOnlyList<WorkbookSheetPreviewDto> Sheets);

public interface IWorkbookImportService
{
    Task<WorkbookInspectionDto> InspectAsync(Stream stream, string fileName, long fileSize, int previewRows = 8, int previewColumns = 20, CancellationToken cancellationToken = default);
}
