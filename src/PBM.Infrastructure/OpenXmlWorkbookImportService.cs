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
            var name = sheet.Name?.Value ?? "Sheet";
            var classification = Classify(name);
            var relationshipId = sheet.Id?.Value ?? throw new InvalidDataException("Worksheet relationship is missing.");
            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart) continue;
            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            if (sheetData is null)
            {
                sheets.Add(new WorkbookSheetPreviewDto(name, 0, 0, [], classification.Profile, classification.ModelCode, classification.Confidence, classification.Tags));
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

            sheets.Add(new WorkbookSheetPreviewDto(name, rows.Count, maxColumn, preview, classification.Profile, classification.ModelCode, classification.Confidence, classification.Tags));
        }

        return Task.FromResult(new WorkbookInspectionDto(fileName, fileSize, sheets));
    }

    private static (WorkbookTemplateProfile Profile, string? ModelCode, int Confidence, IReadOnlyList<string> Tags) Classify(string name)
    {
        var normalized = Normalize(name);
        if (normalized.Contains("اطلاعات پایه")) return (WorkbookTemplateProfile.ReferenceData, "MASTERDATA", 98, ["داده پایه", "Dimension", "Reference"]);
        if (normalized.Contains("واردات وفروش") || normalized.Contains("واردات و فروش")) return (WorkbookTemplateProfile.TradePlan, "TRADE", 98, ["واردات", "فروش", "کالا", "ماهانه"]);
        if (normalized.StartsWith("قیمت")) return (WorkbookTemplateProfile.ProductPrice, "TRADE", 98, ["نرخ فروش", "بهای تمام‌شده", "مارژین", "کالا"]);
        if (normalized.Contains("ریزخريد") || normalized.Contains("ریز خرید")) return (WorkbookTemplateProfile.PurchaseDetail, "TRADE", 99, ["خرید", "کمپانی", "کالا", "ارزی", "ریالی"]);
        if (normalized.Contains("ریز گردش کالا")) return (WorkbookTemplateProfile.InventoryMovement, "TRADE", 99, ["موجودی", "خرید", "فروش", "گردش کالا"]);
        if (normalized.Contains("نيروي انساني") || normalized.Contains("نیروی انسانی")) return (WorkbookTemplateProfile.HumanResources, "HR", 99, ["پرسنل", "واحد", "Headcount"]);
        if (normalized.Contains("تسهیلات مالی") || normalized.Contains("تسهيلات مالي") || normalized.Contains("هزینه های مالی") || normalized.Contains("هزينه هاي مالي")) return (WorkbookTemplateProfile.Financing, "FINANCE", 97, ["تسهیلات", "اصل", "سود", "هزینه مالی"]);
        if (normalized.Contains("سود(زیان)") || normalized.Contains("سود (زیان)") || normalized.Contains("سود و زیان")) return (WorkbookTemplateProfile.ProfitLoss, "FINSTAT", 99, ["صورت سود و زیان", "فروش", "هزینه", "سود"]);
        if (normalized.Contains("ترازنامه")) return (WorkbookTemplateProfile.BalanceSheet, "FINSTAT", 99, ["دارایی", "بدهی", "حقوق صاحبان سهام"]);
        if (normalized.Contains("جريان نقدي") || normalized.Contains("جریان نقدی") || normalized.Contains("نقد عملیات")) return (WorkbookTemplateProfile.CashFlow, "FINSTAT", 99, ["جریان نقد", "عملیاتی", "سرمایه‌گذاری", "تامین مالی"]);
        if (normalized.Contains("مطالبات") || normalized.Contains("بدهیها") || normalized.Contains("دريافتهاو پرداخت") || normalized.Contains("دریافتها و پرداخت")) return (WorkbookTemplateProfile.ReceivablePayable, "FINANCE", 94, ["مطالبات", "بدهی", "دریافت", "پرداخت"]);
        if (normalized.Contains("نسبت ها") || normalized.Contains("نسبت‌ها")) return (WorkbookTemplateProfile.Ratios, "FINSTAT", 96, ["نسبت مالی", "KPI"]);
        if (normalized == "جلد") return (WorkbookTemplateProfile.Cover, null, 100, ["جلد", "متادیتا"]);

        var expenseWords = new[] { "مارکتینگ", "مدیکال", "مدیریت", "مالی", "بازرگانی", "رگولاتوری", "منابع انسانی", "فروش", "هزینه های اداری", "هزینه های ستادی", "هزینه های فروش", "متوسط هزینه های پرسنلی", "سنوات", "ذخیره مرخصی", "عیدی" };
        if (expenseWords.Any(normalized.Contains)) return (WorkbookTemplateProfile.DepartmentExpense, "EXPENSE", 92, ["هزینه", "واحد سازمانی", "ماهانه"]);

        var monthWords = new[] { "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };
        if (monthWords.Any(normalized.Contains)) return (WorkbookTemplateProfile.MonthlyTrade, "TRADE", 82, ["ماهانه", "عملکرد/بودجه"]);

        return (WorkbookTemplateProfile.Unknown, null, 20, ["نیازمند نگاشت دستی"]);
    }

    private static string Normalize(string value) => value.Replace('ي', 'ی').Replace('ك', 'ک').Replace("  ", " ").Trim();

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
