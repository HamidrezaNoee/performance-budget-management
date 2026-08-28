using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class WorkbookNormalizationService : IWorkbookNormalizationService
{
    private static readonly Dictionary<string, string> MonthAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["فروردین"] = "فروردین", ["فروردين"] = "فروردین",
        ["اردیبهشت"] = "اردیبهشت", ["ارديبهشت"] = "اردیبهشت", ["اردی"] = "اردیبهشت",
        ["خرداد"] = "خرداد", ["تیر"] = "تیر", ["تير"] = "تیر", ["مرداد"] = "مرداد",
        ["شهریور"] = "شهریور", ["شهريور"] = "شهریور", ["مهر"] = "مهر", ["آبان"] = "آبان",
        ["آذر"] = "آذر", ["دی"] = "دی", ["دي"] = "دی", ["بهمن"] = "بهمن", ["اسفند"] = "اسفند"
    };

    public Task<WorkbookNormalizationDto> NormalizeAsync(Stream stream, string sheetName, WorkbookTemplateProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sheetName)) throw new ArgumentException("Sheet name is required.");
        if (!stream.CanSeek) throw new ArgumentException("Workbook stream must be seekable.", nameof(stream));
        stream.Position = 0;

        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("Workbook part is missing.");
        var sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault(x => string.Equals(x.Name?.Value?.Trim(), sheetName.Trim(), StringComparison.Ordinal));
        if (sheet is null) throw new KeyNotFoundException($"Worksheet '{sheetName}' was not found.");
        var relationshipId = sheet.Id?.Value ?? throw new InvalidDataException("Worksheet relationship is missing.");
        if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart) throw new InvalidDataException("Worksheet part is missing.");

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(x => x.InnerText).ToArray() ?? [];
        var rows = ReadRows(worksheetPart, sharedStrings);
        var facts = new List<NormalizedWorkbookFactDto>();
        var warnings = new List<string>();
        string? modelCode = null;

        switch (profile)
        {
            case WorkbookTemplateProfile.ProductPrice:
                modelCode = "TRADE";
                NormalizeProductPrice(rows, facts, warnings);
                break;
            case WorkbookTemplateProfile.PurchaseDetail:
                modelCode = "TRADE";
                NormalizePurchaseDetail(rows, facts, warnings);
                break;
            case WorkbookTemplateProfile.TradePlan:
            case WorkbookTemplateProfile.MonthlyTrade:
            case WorkbookTemplateProfile.InventoryMovement:
                modelCode = "TRADE";
                NormalizeTradeMovement(rows, facts, warnings);
                break;
            case WorkbookTemplateProfile.HumanResources:
                modelCode = "HR";
                NormalizeHumanResources(rows, facts, warnings);
                break;
            case WorkbookTemplateProfile.DepartmentExpense:
                modelCode = "EXPENSE";
                NormalizeDepartmentExpense(sheetName, rows, facts, warnings);
                break;
            case WorkbookTemplateProfile.Financing:
            case WorkbookTemplateProfile.ReceivablePayable:
                modelCode = "FINANCE";
                NormalizeFinance(rows, facts, warnings);
                break;
            case WorkbookTemplateProfile.ProfitLoss:
                modelCode = "FINSTAT";
                NormalizeFinancialStatement(rows, facts, warnings, FinancialStatementKind.ProfitLoss, 1, null);
                break;
            case WorkbookTemplateProfile.BalanceSheet:
                modelCode = "FINSTAT";
                NormalizeFinancialStatement(rows, facts, warnings, FinancialStatementKind.BalanceSheet, 1, 19);
                break;
            case WorkbookTemplateProfile.CashFlow:
                modelCode = "FINSTAT";
                NormalizeFinancialStatement(rows, facts, warnings, FinancialStatementKind.CashFlow, 1, null);
                break;
            case WorkbookTemplateProfile.Ratios:
                modelCode = "FINSTAT";
                NormalizeRatios(rows, facts, warnings);
                break;
            default:
                warnings.Add($"Automatic normalization for profile {profile} is not implemented yet; use manual mapping for this sheet.");
                break;
        }

        if (facts.Count == 0 && warnings.Count == 0) warnings.Add("No importable numeric cells were detected in the worksheet.");
        return Task.FromResult(new WorkbookNormalizationDto(sheetName, profile, modelCode, rows.Count, facts, warnings));
    }

    private static void NormalizeProductPrice(IReadOnlyList<SheetRow> rows, ICollection<NormalizedWorkbookFactDto> facts, ICollection<string> warnings)
    {
        var header = rows.FirstOrDefault(r => r.Cells.Values.Count(v => TryExtractMonth(v, out _)) >= 3);
        if (header is null)
        {
            warnings.Add("Product price header with Persian month names was not found.");
            return;
        }

        var mappings = new List<(int Column, string Month, string Measure, string Unit)>();
        foreach (var (column, text) in header.Cells)
        {
            if (column <= 3 || string.IsNullOrWhiteSpace(text) || !TryExtractMonth(text, out var month)) continue;
            var normalized = NormalizeText(text);
            if (normalized.Contains("نرخ فروش")) mappings.Add((column, month, "SALES_PRICE", "IRR"));
            else if (normalized.Contains("تمام شده")) mappings.Add((column, month, "UNIT_COST", "IRR"));
            else if (normalized.Contains("مارژین")) mappings.Add((column, month, "GROSS_MARGIN_PERCENT", "%"));
        }

        foreach (var row in rows.Where(x => x.Index > header.Index))
        {
            var supplier = row.Get(2)?.Trim();
            var product = row.Get(3)?.Trim();
            if (string.IsNullOrWhiteSpace(product)) continue;
            foreach (var mapping in mappings)
            {
                if (!TryDecimal(row.Get(mapping.Column), out var value)) continue;
                facts.Add(new NormalizedWorkbookFactDto(row.Index, mapping.Measure, ValueKind.Budget, mapping.Month, value, mapping.Unit, 1m,
                    new Dictionary<string, string> { ["SUPPLIER"] = supplier ?? "", ["PRODUCT"] = product }, $"{mapping.Month} - {mapping.Measure}"));
            }
        }
    }

    private static void NormalizePurchaseDetail(IReadOnlyList<SheetRow> rows, ICollection<NormalizedWorkbookFactDto> facts, ICollection<string> warnings)
    {
        var metricHeader = rows.OrderByDescending(r => r.Cells.Values.Count(v => IsPurchaseMetric(v))).FirstOrDefault();
        if (metricHeader is null || metricHeader.Cells.Values.Count(v => IsPurchaseMetric(v)) < 3)
        {
            warnings.Add("Purchase quantity/currency/IRR metric header was not detected.");
            return;
        }

        var mappings = new List<(int Column, string Month, string Measure, string Unit, decimal Scale)>();
        foreach (var (column, metricText) in metricHeader.Cells.Where(x => x.Key >= 4 && IsPurchaseMetric(x.Value)))
        {
            var month = FindMonthAround(rows, metricHeader.Index, column);
            if (month is null) continue;
            var metric = NormalizeText(metricText);
            if (metric.Contains("تعداد")) mappings.Add((column, month, "PURCHASE_QTY", "عدد", 1m));
            else if (metric.Contains("ارزی")) mappings.Add((column, month, "PURCHASE_FX", "Currency", 1m));
            else if (metric.Contains("ریالی")) mappings.Add((column, month, "PURCHASE_IRR", "IRR", 1_000_000m));
        }

        if (mappings.Count == 0)
        {
            warnings.Add("Purchase columns were detected but no month label could be resolved for them.");
            return;
        }

        foreach (var row in rows.Where(x => x.Index > metricHeader.Index))
        {
            var supplier = row.Get(2)?.Trim();
            var product = row.Get(3)?.Trim();
            if (string.IsNullOrWhiteSpace(product)) continue;
            foreach (var mapping in mappings)
            {
                if (!TryDecimal(row.Get(mapping.Column), out var raw)) continue;
                facts.Add(new NormalizedWorkbookFactDto(row.Index, mapping.Measure, ValueKind.Budget, mapping.Month, raw * mapping.Scale, mapping.Unit, mapping.Scale,
                    new Dictionary<string, string> { ["SUPPLIER"] = supplier ?? "", ["PRODUCT"] = product }, $"{mapping.Month} - {mapping.Measure}"));
            }
        }
    }

    private static void NormalizeTradeMovement(IReadOnlyList<SheetRow> rows, ICollection<NormalizedWorkbookFactDto> facts, ICollection<string> warnings)
    {
        var metricHeader = rows.OrderByDescending(r => r.Cells.Values.Count(v => MapTradeMeasure(v) is not null)).FirstOrDefault();
        if (metricHeader is null || metricHeader.Cells.Values.Count(v => MapTradeMeasure(v) is not null) < 2)
        {
            warnings.Add("Inventory/import/sales metric header was not detected.");
            return;
        }

        var mappings = metricHeader.Cells
            .Select(x => (Column: x.Key, Text: x.Value, Measure: MapTradeMeasure(x.Value)))
            .Where(x => x.Measure is not null)
            .Select(x => (x.Column, Month: ResolveMonth(rows, metricHeader.Index, x.Column, x.Text), Measure: x.Measure!))
            .Where(x => x.Month is not null)
            .ToList();
        if (mappings.Count == 0)
        {
            warnings.Add("Trade metrics were detected but their Persian months could not be resolved.");
            return;
        }

        var firstMetricColumn = mappings.Min(x => x.Column);
        foreach (var row in rows.Where(x => x.Index > metricHeader.Index))
        {
            var labels = row.Cells.Where(x => x.Key < firstMetricColumn && !string.IsNullOrWhiteSpace(x.Value)).OrderBy(x => x.Key).Select(x => x.Value!.Trim()).ToArray();
            if (labels.Length == 0) continue;
            var product = labels[^1];
            var supplier = labels.Length >= 2 ? labels[^2] : string.Empty;
            if (IsTotalOrHeading(product)) continue;
            foreach (var mapping in mappings)
            {
                if (!TryDecimal(row.Get(mapping.Column), out var raw)) continue;
                facts.Add(new NormalizedWorkbookFactDto(row.Index, mapping.Measure, ValueKind.Budget, mapping.Month, raw, "عدد", 1m,
                    new Dictionary<string, string> { ["SUPPLIER"] = supplier, ["PRODUCT"] = product }, $"{mapping.Month} - {mapping.Measure}"));
            }
        }
    }

    private static string? MapTradeMeasure(string? value)
    {
        var x = NormalizeText(value);
        if (x.Contains("موجودی اول") || x.Contains("موجودي اول") || x.Contains("ابتدای دوره")) return "OPENING_QTY";
        if (x.Contains("فروش رایگان") || x.Contains("فروش رايگان") || x.Contains("آفر")) return "FREE_SALES_QTY";
        if (x.Contains("فروش") && !x.Contains("نرخ") && !x.Contains("مبلغ")) return "SALES_QTY";
        if (x.Contains("واردات") || x.Contains("خرید") || x.Contains("خريد")) return "IMPORT_QTY";
        if (x.Contains("نمونه")) return "SAMPLE_QTY";
        if (x.Contains("ضایعات") || x.Contains("ضايعات")) return "WASTE_QTY";
        if (x.Contains("موجودی پایان") || x.Contains("موجودي پايان") || x.Contains("پایان دوره")) return "CLOSING_QTY";
        return null;
    }

    private static void NormalizeHumanResources(IReadOnlyList<SheetRow> rows, ICollection<NormalizedWorkbookFactDto> facts, ICollection<string> warnings)
    {
        var header = FindMonthHeader(rows);
        if (header is null)
        {
            warnings.Add("Human-resource month header was not found.");
            return;
        }
        var months = header.Cells.Where(x => TryExtractMonth(x.Value, out _)).Select(x => (x.Key, Month: ExtractMonth(x.Value)!)).ToList();
        var firstMonthColumn = months.Min(x => x.Key);
        string? currentDepartment = null;

        foreach (var row in rows.Where(x => x.Index > header.Index))
        {
            var prefix = row.Cells.Where(x => x.Key < firstMonthColumn && !string.IsNullOrWhiteSpace(x.Value)).OrderBy(x => x.Key).ToArray();
            if (prefix.Length == 0) continue;
            var metric = prefix.Select(x => (Text: x.Value!, Measure: MapHrMeasure(x.Value))).FirstOrDefault(x => x.Measure is not null);
            var departmentCandidate = prefix.Where(x => metric.Text is null || !string.Equals(x.Value, metric.Text, StringComparison.Ordinal)).Select(x => x.Value?.Trim()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (metric.Measure is null)
            {
                if (departmentCandidate is not null && !IsTotalOrHeading(departmentCandidate)) currentDepartment = departmentCandidate;
                continue;
            }
            var department = departmentCandidate ?? currentDepartment;
            if (string.IsNullOrWhiteSpace(department))
            {
                warnings.Add($"ردیف {row.Index}: واحد سازمانی برای شاخص نیروی انسانی مشخص نیست.");
                continue;
            }
            currentDepartment = department;
            foreach (var (column, month) in months)
            {
                if (!TryDecimal(row.Get(column), out var raw)) continue;
                facts.Add(new NormalizedWorkbookFactDto(row.Index, metric.Measure, ValueKind.Budget, month, raw, "نفر", 1m,
                    new Dictionary<string, string> { ["DEPARTMENT"] = department }, metric.Text));
            }
        }
        if (facts.Count == 0) warnings.Add("No recognizable headcount rows were found.");
    }

    private static string? MapHrMeasure(string? value)
    {
        var x = NormalizeText(value);
        if (x.Contains("ابتدای دوره") || x.Contains("اول دوره") || x.Contains("ابتدا")) return "OPENING_HEADCOUNT";
        if (x.Contains("استخدام") || x.Contains("افزایش") || x.Contains("افزايش") || x.Contains("ورودی") || x.Contains("ورودي")) return "HIRES";
        if (x.Contains("کاهش") || x.Contains("خروج") || x.Contains("ترک")) return "TERMINATIONS";
        if (x.Contains("پایان دوره") || x.Contains("پايان دوره") || x.Contains("پایان") || x.Contains("پايان")) return "CLOSING_HEADCOUNT";
        return null;
    }

    private static void NormalizeDepartmentExpense(string sheetName, IReadOnlyList<SheetRow> rows, ICollection<NormalizedWorkbookFactDto> facts, ICollection<string> warnings)
    {
        var header = FindMonthHeader(rows);
        if (header is null)
        {
            warnings.Add("Monthly expense header was not found.");
            return;
        }

        var monthColumns = header.Cells.Select(x => (Column: x.Key, Text: x.Value)).Where(x => TryExtractMonth(x.Text, out _))
            .Select(x => (x.Column, Month: ExtractMonth(x.Text)!)).ToList();
        var department = CleanDepartmentName(sheetName);
        var scale = DetectMoneyScale(rows);

        foreach (var row in rows.Where(x => x.Index > header.Index))
        {
            var account = row.Get(1)?.Trim();
            if (string.IsNullOrWhiteSpace(account) || IsTotalOrHeading(account)) continue;
            foreach (var (column, month) in monthColumns)
            {
                if (!TryDecimal(row.Get(column), out var raw)) continue;
                facts.Add(new NormalizedWorkbookFactDto(row.Index, "EXPENSE_AMOUNT", ValueKind.Budget, month, raw * scale, "IRR", scale,
                    new Dictionary<string, string> { ["DEPARTMENT"] = department, ["ACCOUNT"] = account }, account));
            }
        }
    }

    private static void NormalizeFinance(IReadOnlyList<SheetRow> rows, ICollection<NormalizedWorkbookFactDto> facts, ICollection<string> warnings)
    {
        var header = FindMonthHeader(rows);
        if (header is null)
        {
            warnings.Add("Finance month header was not found.");
            return;
        }
        var months = header.Cells.Where(x => TryExtractMonth(x.Value, out _)).Select(x => (x.Key, Month: ExtractMonth(x.Value)!)).ToList();
        var firstMonthColumn = months.Min(x => x.Key);
        var scale = DetectMoneyScale(rows);

        foreach (var row in rows.Where(x => x.Index > header.Index))
        {
            var label = row.Cells.Where(x => x.Key < firstMonthColumn && !string.IsNullOrWhiteSpace(x.Value)).OrderBy(x => x.Key).Select(x => x.Value!.Trim()).LastOrDefault();
            if (string.IsNullOrWhiteSpace(label) || IsTotalOrHeading(label)) continue;
            var normalized = NormalizeText(label);
            var isRate = normalized.Contains("نرخ") || normalized.Contains("درصد") || normalized.Contains("%") || normalized.Contains("سود تسهیلات");
            var measure = isRate ? "FINANCE_RATE" : "FINANCE_AMOUNT";
            var unit = isRate ? "%" : "IRR";
            var appliedScale = isRate ? 1m : scale;
            foreach (var (column, month) in months)
            {
                if (!TryDecimal(row.Get(column), out var raw)) continue;
                facts.Add(new NormalizedWorkbookFactDto(row.Index, measure, ValueKind.Budget, month, raw * appliedScale, unit, appliedScale,
                    new Dictionary<string, string> { ["ACCOUNT"] = label }, label));
            }
        }
        if (facts.Count == 0) warnings.Add("No recognizable finance rows were found.");
    }

    private static void NormalizeRatios(IReadOnlyList<SheetRow> rows, ICollection<NormalizedWorkbookFactDto> facts, ICollection<string> warnings)
    {
        var header = FindMonthHeader(rows);
        if (header is null)
        {
            warnings.Add("Financial-ratio month header was not found.");
            return;
        }
        var months = header.Cells.Where(x => TryExtractMonth(x.Value, out _)).Select(x => (x.Key, Month: ExtractMonth(x.Value)!)).ToList();
        var firstMonthColumn = months.Min(x => x.Key);
        foreach (var row in rows.Where(x => x.Index > header.Index))
        {
            var label = row.Cells.Where(x => x.Key < firstMonthColumn && !string.IsNullOrWhiteSpace(x.Value)).OrderBy(x => x.Key).Select(x => x.Value!.Trim()).LastOrDefault();
            if (string.IsNullOrWhiteSpace(label) || IsTotalOrHeading(label)) continue;
            foreach (var (column, month) in months)
            {
                if (!TryDecimal(row.Get(column), out var raw)) continue;
                facts.Add(new NormalizedWorkbookFactDto(row.Index, "FINANCIAL_RATIO", ValueKind.Budget, month, raw, "%", 1m,
                    new Dictionary<string, string> { ["ACCOUNT"] = label }, label));
            }
        }
        if (facts.Count == 0) warnings.Add("No financial-ratio values were detected.");
    }

    private static void NormalizeFinancialStatement(IReadOnlyList<SheetRow> rows, ICollection<NormalizedWorkbookFactDto> facts, ICollection<string> warnings, FinancialStatementKind kind, int firstLabelColumn, int? secondLabelColumn)
    {
        var header = FindMonthHeader(rows);
        if (header is null)
        {
            warnings.Add("Financial statement month header was not found.");
            return;
        }

        var scale = DetectMoneyScale(rows);
        NormalizeFinancialStatementSide(rows, header, firstLabelColumn, secondLabelColumn is null ? int.MaxValue : secondLabelColumn.Value - 1, kind, facts, scale);
        if (secondLabelColumn.HasValue) NormalizeFinancialStatementSide(rows, header, secondLabelColumn.Value, int.MaxValue, kind, facts, scale);
        if (facts.Count == 0) warnings.Add("No recognized financial statement rows were found. Additional account aliases may be required.");
    }

    private static void NormalizeFinancialStatementSide(IReadOnlyList<SheetRow> rows, SheetRow header, int labelColumn, int maxColumn, FinancialStatementKind kind, ICollection<NormalizedWorkbookFactDto> facts, decimal scale)
    {
        var monthColumns = header.Cells.Where(x => x.Key > labelColumn && x.Key <= maxColumn && TryExtractMonth(x.Value, out _))
            .Select(x => (x.Key, Month: ExtractMonth(x.Value)!)).ToList();
        foreach (var row in rows.Where(x => x.Index > header.Index))
        {
            var label = row.Get(labelColumn)?.Trim();
            if (string.IsNullOrWhiteSpace(label)) continue;
            var accountCode = MapFinancialAccount(label, kind);
            if (accountCode is null) continue;
            foreach (var (column, month) in monthColumns)
            {
                if (!TryDecimal(row.Get(column), out var raw)) continue;
                facts.Add(new NormalizedWorkbookFactDto(row.Index, "STATEMENT_AMOUNT", ValueKind.Budget, month, raw * scale, "IRR", scale,
                    new Dictionary<string, string> { ["ACCOUNT"] = accountCode }, label));
            }
        }
    }

    private static string? MapFinancialAccount(string label, FinancialStatementKind kind)
    {
        var x = NormalizeText(label);
        if (kind == FinancialStatementKind.ProfitLoss)
        {
            if (x.Contains("فروش ناخالص")) return "GROSS_SALES";
            if (x.Contains("تخفیفات فروش")) return "SALES_DISCOUNT";
            if (x.Contains("فروش خالص")) return "NET_SALES";
            if (x.Contains("قيمت تمام شده") || x.Contains("قیمت تمام شده")) return "COGS";
            if (x.Contains("ناخالص")) return "GROSS_PROFIT";
            if (x.Contains("اداري") || x.Contains("اداری") || x.Contains("عمومي") || x.Contains("عمومی")) return "ADMIN_EXPENSE";
            if (x.Contains("عملياتي") || x.Contains("عملیاتی")) return "OPERATING_PROFIT";
            if (x.Contains("هزینه های مالی") || x.Contains("هزينه هاي مالي")) return "FINANCE_COST";
            if (x.Contains("قبل از مالیات") || x.Contains("قبل از ماليات")) return "PROFIT_BEFORE_TAX";
            if (x == "مالیات" || x == "ماليات") return "TAX";
            if (x.Contains("سود خالص")) return "NET_PROFIT";
        }
        else if (kind == FinancialStatementKind.BalanceSheet)
        {
            if (x.Contains("نقد") && x.Contains("بانک")) return "CASH_BANK";
            if (x.Contains("دريافتني تجاري") || x.Contains("دریافتنی تجاری")) return "TRADE_RECEIVABLE";
            if (x.Contains("موجودی مواد") || x.Contains("موجودي مواد")) return "INVENTORY";
            if (x.Contains("جمع داراییهای جاری") || x.Contains("جمع داراييهاي جاري") || x.Contains("جمع دارایی های جاری")) return "CURRENT_ASSETS";
            if (x.Contains("جمع داراییها") || x.Contains("جمع داراييها") || x.Contains("جمع دارایی ها")) return "TOTAL_ASSETS";
            if (x.Contains("پرداختني تجاري") || x.Contains("پرداختنی تجاری")) return "TRADE_PAYABLE";
            if (x.Contains("جمع بدهي هاي جاري") || x.Contains("جمع بدهی های جاری")) return "CURRENT_LIABILITIES";
            if (x.Contains("جمع حقوق صاحبان سهام")) return "EQUITY";
            if (x.Contains("جمع بدهيها و حقوق") || x.Contains("جمع بدهیها و حقوق") || x.Contains("جمع بدهی ها و حقوق")) return "TOTAL_LIAB_EQUITY";
        }
        else
        {
            if ((x.Contains("خالص") && x.Contains("عملیاتی")) || (x.Contains("خالص") && x.Contains("عملياتي"))) return "CFO";
            if ((x.Contains("خالص") && x.Contains("سرمایه گذاری")) || (x.Contains("خالص") && x.Contains("سرمايه گذاري"))) return "CFI";
            if ((x.Contains("خالص") && x.Contains("تامین مالی")) || (x.Contains("خالص") && x.Contains("تامين مالي"))) return "CFF";
            if ((x.Contains("مانده") && x.Contains("نقد") && x.Contains("پایان")) || (x.Contains("مانده") && x.Contains("نقد") && x.Contains("پايان"))) return "ENDING_CASH";
        }
        return null;
    }

    private static decimal DetectMoneyScale(IReadOnlyList<SheetRow> rows)
    {
        var headerText = string.Join(' ', rows.Take(15).SelectMany(x => x.Cells.Values).Where(x => !string.IsNullOrWhiteSpace(x)));
        var normalized = NormalizeText(headerText);
        if (normalized.Contains("میلیارد ریال") || normalized.Contains("ميليارد ريال")) return 1_000_000_000m;
        if (normalized.Contains("میلیون ریال") || normalized.Contains("ميليون ريال")) return 1_000_000m;
        if (normalized.Contains("هزار ریال") || normalized.Contains("هزار ريال")) return 1_000m;
        return 1m;
    }

    private static SheetRow? FindMonthHeader(IReadOnlyList<SheetRow> rows) => rows.OrderByDescending(r => r.Cells.Values.Count(v => TryExtractMonth(v, out _))).FirstOrDefault(r => r.Cells.Values.Count(v => TryExtractMonth(v, out _)) >= 3);

    private static string? ResolveMonth(IReadOnlyList<SheetRow> rows, int headerRow, int column, string? currentText)
    {
        if (TryExtractMonth(currentText, out var month)) return month;
        return FindMonthAround(rows, headerRow, column);
    }

    private static string? FindMonthAround(IReadOnlyList<SheetRow> rows, int headerRow, int column)
    {
        for (var r = headerRow - 1; r >= Math.Max(1, headerRow - 4); r--)
        {
            var row = rows.FirstOrDefault(x => x.Index == r);
            if (row is null) continue;
            for (var c = column; c >= Math.Max(1, column - 2); c--)
                if (TryExtractMonth(row.Get(c), out var month)) return month;
        }
        return null;
    }

    private static bool IsPurchaseMetric(string? text)
    {
        var x = NormalizeText(text);
        return x == "تعداد" || x.Contains("مبلغ ارزی") || x.Contains("مبلغ ارزي") || x.Contains("مبلغ ریالی") || x.Contains("مبلغ ريالي");
    }

    private static string CleanDepartmentName(string sheetName)
    {
        var x = NormalizeText(sheetName).Replace("بودجه هزینه های", "", StringComparison.OrdinalIgnoreCase).Replace("بودجه هزينه هاي", "", StringComparison.OrdinalIgnoreCase).Trim();
        return x switch
        {
            "مارکتینگ - بیمارستانی" or "مارکتینگ-بیمارستانی" => "مارکتینگ - بیمارستانی",
            "مارکتینگ -رکورداتی" or "مارکتینگ-رکورداتی" => "مارکتینگ - رکورداتی",
            "مارکتینگ-چشمی" or "مارکتینگ - چشمی" => "مارکتینگ - چشمی",
            _ => x
        };
    }

    private static bool IsTotalOrHeading(string text)
    {
        var x = NormalizeText(text);
        return x.StartsWith("جمع ") || x.StartsWith("جمع کل") || x.EndsWith(":") || x.Contains("شرح هزینه");
    }

    private static bool TryExtractMonth(string? value, out string month)
    {
        month = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = NormalizeText(value);
        foreach (var (alias, canonical) in MonthAliases.OrderByDescending(x => x.Key.Length))
        {
            if (normalized.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                month = canonical;
                return true;
            }
        }
        return false;
    }

    private static string? ExtractMonth(string? value) => TryExtractMonth(value, out var month) ? month : null;

    private static bool TryDecimal(string? value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('=')) return false;
        var cleaned = ToLatinDigits(value).Replace(",", "").Replace("٬", "").Replace("٪", "").Replace("%", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out result)
            || decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.GetCultureInfo("fa-IR"), out result);
    }

    private static string ToLatinDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '۰' and <= '۹') chars[i] = (char)('0' + chars[i] - '۰');
            else if (chars[i] is >= '٠' and <= '٩') chars[i] = (char)('0' + chars[i] - '٠');
        }
        return new string(chars);
    }

    private static string NormalizeText(string? value) => (value ?? "").Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ').Replace("  ", " ").Trim();

    private static List<SheetRow> ReadRows(WorksheetPart worksheetPart, IReadOnlyList<string> sharedStrings)
    {
        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
        if (sheetData is null) return [];
        var result = new List<SheetRow>();
        foreach (var row in sheetData.Elements<Row>())
        {
            var cells = new Dictionary<int, string?>();
            foreach (var cell in row.Elements<Cell>())
            {
                var column = GetColumnIndex(cell.CellReference?.Value);
                if (column > 0) cells[column] = GetCellValue(cell, sharedStrings);
            }
            result.Add(new SheetRow((int)(row.RowIndex?.Value ?? (uint)(result.Count + 1)), cells));
        }
        return result;
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

    private sealed record SheetRow(int Index, IReadOnlyDictionary<int, string?> Cells)
    {
        public string? Get(int column) => Cells.TryGetValue(column, out var value) ? value : null;
    }

    private enum FinancialStatementKind { ProfitLoss, BalanceSheet, CashFlow }
}
