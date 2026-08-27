using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PBM.Application;

namespace PBM.Infrastructure;

public sealed class OpenXmlWorkbookImportService : IWorkbookImportService
{
    public Task<WorkbookInspectionDto> InspectAsync(Stream stream, string fileName, long fileSize, int previewRows = 8, int previewColumns = 20, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanSeek) throw new ArgumentException("Workbook stream must be seekable.", nameof(stream));
        stream.Position = 0;

        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("The XLSX file does not contain a workbook part.");
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(x => x.InnerText).ToArray() ?? [];
        var sheets = new List<WorkbookSheetPreviewDto>();

        foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationshipId = sheet.Id?.Value ?? throw new InvalidDataException("Worksheet relationship is missing.");
            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart) continue;
            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            if (sheetData is null)
            {
                sheets.Add(new WorkbookSheetPreviewDto(sheet.Name?.Value ?? "Sheet", 0, 0, []));
                continue;
            }

            var rows = sheetData.Elements<Row>().ToList();
            var maxColumn = 0;
            foreach (var cell in rows.SelectMany(x => x.Elements<Cell>()))
                maxColumn = Math.Max(maxColumn, GetColumnIndex(cell.CellReference?.Value));

            var preview = new List<IReadOnlyList<string?>>();
            foreach (var row in rows.Take(Math.Max(1, previewRows)))
            {
                var values = Enumerable.Repeat<string?>(null, Math.Min(Math.Max(maxColumn, 1), Math.Max(previewColumns, 1))).ToArray();
                foreach (var cell in row.Elements<Cell>())
                {
                    var columnIndex = GetColumnIndex(cell.CellReference?.Value) - 1;
                    if (columnIndex < 0 || columnIndex >= values.Length) continue;
                    values[columnIndex] = GetCellValue(cell, sharedStrings);
                }
                preview.Add(values);
            }

            sheets.Add(new WorkbookSheetPreviewDto(sheet.Name?.Value ?? "Sheet", rows.Count, maxColumn, preview));
        }

        return Task.FromResult(new WorkbookInspectionDto(fileName, fileSize, sheets));
    }

    private static string? GetCellValue(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.InnerText;
        var raw = cell.CellValue?.Text;
        if (raw is null) return cell.InnerText is { Length: > 0 } inner ? inner : null;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(raw, out var index) && index >= 0 && index < sharedStrings.Count) return sharedStrings[index];
        if (cell.DataType?.Value == CellValues.Boolean) return raw == "1" ? "TRUE" : "FALSE";
        return raw;
    }

    private static int GetColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return 0;
        var value = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            value = value * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return value;
    }
}
