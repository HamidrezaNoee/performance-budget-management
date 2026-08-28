using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PBM.Application;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class WorkbookNormalizationTests
{
    private readonly WorkbookNormalizationService _service = new();

    [Fact]
    public async Task Human_resources_sheet_maps_headcount_measures_by_department_and_month()
    {
        using var stream = BuildWorkbook("نیروی انسانی",
            ["شرح واحد", "شاخص", "فروردین", "اردیبهشت", "خرداد"],
            ["منابع انسانی", "ابتدای دوره", "10", "11", "12"],
            ["منابع انسانی", "استخدام", "2", "1", "0"],
            ["منابع انسانی", "خروج", "1", "0", "1"],
            ["منابع انسانی", "پایان دوره", "11", "12", "11"]);

        var result = await _service.NormalizeAsync(stream, "نیروی انسانی", WorkbookTemplateProfile.HumanResources);

        Assert.Equal("HR", result.ModelCode);
        Assert.Equal(12, result.Facts.Count);
        Assert.Contains(result.Facts, x => x.MeasureCode == "OPENING_HEADCOUNT" && x.PeriodName == "فروردین" && x.Value == 10m);
        Assert.Contains(result.Facts, x => x.MeasureCode == "HIRES" && x.PeriodName == "اردیبهشت" && x.Value == 1m);
        Assert.All(result.Facts, x => Assert.Equal("منابع انسانی", x.DimensionMembers["DEPARTMENT"]));
    }

    [Fact]
    public async Task Finance_sheet_detects_million_rial_scale_and_rate_rows()
    {
        using var stream = BuildWorkbook("تسهیلات مالی",
            ["واحد: میلیون ریال", "", "", ""],
            ["شرح", "فروردین", "اردیبهشت", "خرداد"],
            ["بازپرداخت اصل تسهیلات", "150", "200", "250"],
            ["نرخ سود تسهیلات", "18", "18", "18"]);

        var result = await _service.NormalizeAsync(stream, "تسهیلات مالی", WorkbookTemplateProfile.Financing);

        Assert.Equal("FINANCE", result.ModelCode);
        Assert.Contains(result.Facts, x => x.MeasureCode == "FINANCE_AMOUNT" && x.PeriodName == "فروردین" && x.Value == 150_000_000m && x.ScaleApplied == 1_000_000m);
        Assert.Contains(result.Facts, x => x.MeasureCode == "FINANCE_RATE" && x.PeriodName == "خرداد" && x.Value == 18m && x.ScaleApplied == 1m);
    }

    [Fact]
    public async Task Inventory_movement_sheet_maps_trade_quantity_measures()
    {
        using var stream = BuildWorkbook("ریز گردش کالا",
            ["", "", "فروردین", "فروردین", "فروردین"],
            ["کمپانی", "کالا", "موجودی اول دوره", "فروش", "موجودی پایان دوره"],
            ["Chiesi", "Bramitob", "100", "25", "75"]);

        var result = await _service.NormalizeAsync(stream, "ریز گردش کالا", WorkbookTemplateProfile.InventoryMovement);

        Assert.Equal("TRADE", result.ModelCode);
        Assert.Equal(3, result.Facts.Count);
        Assert.Contains(result.Facts, x => x.MeasureCode == "OPENING_QTY" && x.Value == 100m);
        Assert.Contains(result.Facts, x => x.MeasureCode == "SALES_QTY" && x.Value == 25m);
        Assert.Contains(result.Facts, x => x.MeasureCode == "CLOSING_QTY" && x.Value == 75m);
        Assert.All(result.Facts, x => Assert.Equal("Bramitob", x.DimensionMembers["PRODUCT"]));
    }

    [Fact]
    public async Task Financial_ratios_are_kept_as_percentages_without_money_scaling()
    {
        using var stream = BuildWorkbook("نسبت ها",
            ["نسبت", "فروردین", "اردیبهشت", "خرداد"],
            ["حاشیه سود خالص", "12.5", "13", "14.25"]);

        var result = await _service.NormalizeAsync(stream, "نسبت ها", WorkbookTemplateProfile.Ratios);

        Assert.Equal("FINSTAT", result.ModelCode);
        Assert.Equal(3, result.Facts.Count);
        Assert.All(result.Facts, x => Assert.Equal("FINANCIAL_RATIO", x.MeasureCode));
        Assert.Contains(result.Facts, x => x.PeriodName == "خرداد" && x.Value == 14.25m && x.Unit == "%");
    }

    private static MemoryStream BuildWorkbook(string sheetName, params string[][] rows)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = new Row { RowIndex = (uint)(rowIndex + 1) };
                for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
                {
                    var value = rows[rowIndex][columnIndex];
                    var reference = $"{ColumnName(columnIndex + 1)}{rowIndex + 1}";
                    row.Append(new Cell
                    {
                        CellReference = reference,
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(value ?? string.Empty))
                    });
                }
                sheetData.Append(row);
            }

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1U, Name = sheetName });
            workbookPart.Workbook.Save();
            worksheetPart.Worksheet.Save();
        }
        stream.Position = 0;
        return stream;
    }

    private static string ColumnName(int column)
    {
        var result = string.Empty;
        while (column > 0)
        {
            column--;
            result = (char)('A' + column % 26) + result;
            column /= 26;
        }
        return result;
    }
}
