using PBM.Domain;

namespace PBM.Application;

public static class DriverTemplateCatalog
{
    private static readonly IReadOnlyList<DriverTemplateDto> Templates =
    [
        new(
            "SALES_REVENUE",
            "فروش و درآمد",
            "الگوی Driver-Based برای تعداد فروش، قیمت، تخفیف، فروش خالص و رشد سناریویی.",
            ["TRADE", "SALES"],
            [
                Assumption("DISCOUNT_RATE", "نرخ تخفیف فروش", "%", "درصد میانگین تخفیف فروش."),
                Assumption("SALES_GROWTH_RATE", "نرخ رشد فروش", "%", "فرض رشد فروش نسبت به مبنای برنامه.")
            ],
            [
                Manual("SALES_QTY", "تعداد فروش", "عدد", MeasureValueType.Quantity, 10),
                Manual("UNIT_LIST_PRICE", "قیمت لیست واحد", "ریال", MeasureValueType.Rate, 20, MeasureAggregation.Average),
                Calc("GROSS_REVENUE", "فروش ناخالص", "ریال", MeasureValueType.Amount, 30, "[SALES_QTY] * [UNIT_LIST_PRICE]"),
                Calc("DISCOUNT_AMOUNT", "مبلغ تخفیف", "ریال", MeasureValueType.Amount, 40, "[GROSS_REVENUE] * [ASSUMP:DISCOUNT_RATE] / 100"),
                Calc("NET_REVENUE", "فروش خالص", "ریال", MeasureValueType.Amount, 50, "[GROSS_REVENUE] - [DISCOUNT_AMOUNT]"),
                Calc("GROWTH_ADJ_REVENUE", "فروش خالص تعدیل‌شده با رشد", "ریال", MeasureValueType.Amount, 60, "[NET_REVENUE] + ([NET_REVENUE] * [ASSUMP:SALES_GROWTH_RATE] / 100)")
            ]),

        new(
            "PAYROLL",
            "حقوق و نیروی انسانی",
            "الگوی Headcount و حقوق ماهانه با افزایش حقوق و محاسبه هزینه پرسنلی.",
            ["HR", "EXPENSE"],
            [
                Assumption("SALARY_GROWTH_RATE", "نرخ افزایش حقوق", "%", "فرض افزایش حقوق و مزایا."),
                Assumption("HEADCOUNT_GROWTH_RATE", "نرخ رشد تعداد کارکنان", "%", "فرض رشد Headcount؛ برای سناریوهای نیروی انسانی قابل استفاده است.")
            ],
            [
                Manual("OPENING_HEADCOUNT", "تعداد ابتدای دوره", "نفر", MeasureValueType.Quantity, 10),
                Manual("HIRES", "استخدام", "نفر", MeasureValueType.Quantity, 20),
                Manual("TERMINATIONS", "خروج نیرو", "نفر", MeasureValueType.Quantity, 30),
                Calc("CLOSING_HEADCOUNT", "تعداد پایان دوره", "نفر", MeasureValueType.Quantity, 40, "[OPENING_HEADCOUNT] + [HIRES] - [TERMINATIONS]"),
                Calc("AVERAGE_HEADCOUNT", "میانگین تعداد کارکنان", "نفر", MeasureValueType.Quantity, 50, "[OPENING_HEADCOUNT] + ([CLOSING_HEADCOUNT] - [OPENING_HEADCOUNT]) / 2", MeasureAggregation.Average),
                Manual("BASE_MONTHLY_SALARY", "حقوق پایه ماهانه هر نفر", "ریال", MeasureValueType.Rate, 60, MeasureAggregation.Average),
                Calc("ADJUSTED_MONTHLY_SALARY", "حقوق ماهانه تعدیل‌شده", "ریال", MeasureValueType.Rate, 70, "[BASE_MONTHLY_SALARY] + ([BASE_MONTHLY_SALARY] * [ASSUMP:SALARY_GROWTH_RATE] / 100)", MeasureAggregation.Average),
                Calc("PAYROLL_COST", "هزینه حقوق و مزایا", "ریال", MeasureValueType.Amount, 80, "[AVERAGE_HEADCOUNT] * [ADJUSTED_MONTHLY_SALARY]")
            ]),

        new(
            "IMPORT_LANDED_COST",
            "بهای تمام‌شده واردات",
            "الگوی واردات برای مقدار، قیمت ارزی، نرخ تبدیل، حقوق گمرکی، حمل، بیمه و Landed Cost.",
            ["TRADE", "IMPORT"],
            [
                Assumption("CUSTOMS_RATE", "نرخ حقوق و عوارض گمرکی", "%", "درصد پایه حقوق و عوارض گمرکی."),
                Assumption("FREIGHT_GROWTH_RATE", "نرخ رشد هزینه حمل", "%", "نرخ رشد هزینه حمل نسبت به مبنا.")
            ],
            [
                Manual("IMPORT_QTY", "تعداد واردات", "عدد", MeasureValueType.Quantity, 10),
                Manual("FX_UNIT_COST", "قیمت ارزی واحد", "ارز", MeasureValueType.Rate, 20, MeasureAggregation.Average),
                Calc("FX_PURCHASE_AMOUNT", "مبلغ ارزی خرید", "ارز", MeasureValueType.Amount, 30, "[IMPORT_QTY] * [FX_UNIT_COST]"),
                Manual("BUDGET_FX_RATE", "نرخ ارز بودجه", "ریال/ارز", MeasureValueType.Rate, 40, MeasureAggregation.Average),
                Calc("PURCHASE_IRR", "مبلغ ریالی خرید", "ریال", MeasureValueType.Amount, 50, "[FX_PURCHASE_AMOUNT] * [BUDGET_FX_RATE]"),
                Calc("CUSTOMS_DUTY", "حقوق و عوارض گمرکی", "ریال", MeasureValueType.Amount, 60, "[PURCHASE_IRR] * [ASSUMP:CUSTOMS_RATE] / 100"),
                Manual("FREIGHT_COST", "هزینه حمل", "ریال", MeasureValueType.Amount, 70),
                Calc("FREIGHT_COST_ADJ", "هزینه حمل تعدیل‌شده", "ریال", MeasureValueType.Amount, 80, "[FREIGHT_COST] + ([FREIGHT_COST] * [ASSUMP:FREIGHT_GROWTH_RATE] / 100)"),
                Manual("INSURANCE_COST", "هزینه بیمه", "ریال", MeasureValueType.Amount, 90),
                Calc("LANDED_COST_TOTAL", "کل بهای تمام‌شده واردات", "ریال", MeasureValueType.Amount, 100, "[PURCHASE_IRR] + [CUSTOMS_DUTY] + [FREIGHT_COST_ADJ] + [INSURANCE_COST]"),
                Calc("LANDED_UNIT_COST", "بهای تمام‌شده واحد", "ریال", MeasureValueType.Rate, 110, "[LANDED_COST_TOTAL] / MAX([IMPORT_QTY], 1)", MeasureAggregation.Average)
            ]),

        new(
            "FINANCING",
            "تأمین مالی و تسهیلات",
            "الگوی مانده بدهی، دریافت تسهیلات، بازپرداخت اصل، نرخ تأمین مالی و هزینه بهره.",
            ["FINANCE"],
            [
                Assumption("FINANCE_RATE", "نرخ هزینه تأمین مالی", "%", "نرخ سالانه هزینه تأمین مالی/بهره.")
            ],
            [
                Manual("OPENING_DEBT", "مانده بدهی ابتدای دوره", "ریال", MeasureValueType.Amount, 10),
                Manual("DRAWDOWN", "دریافت تسهیلات", "ریال", MeasureValueType.Amount, 20),
                Manual("PRINCIPAL_REPAYMENT", "بازپرداخت اصل", "ریال", MeasureValueType.Amount, 30),
                Calc("CLOSING_DEBT", "مانده بدهی پایان دوره", "ریال", MeasureValueType.Amount, 40, "[OPENING_DEBT] + [DRAWDOWN] - [PRINCIPAL_REPAYMENT]"),
                Calc("AVERAGE_DEBT", "میانگین مانده بدهی", "ریال", MeasureValueType.Amount, 50, "([OPENING_DEBT] + [CLOSING_DEBT]) / 2", MeasureAggregation.Average),
                Calc("INTEREST_EXPENSE", "هزینه تأمین مالی ماهانه", "ریال", MeasureValueType.Amount, 60, "[AVERAGE_DEBT] * [ASSUMP:FINANCE_RATE] / 1200"),
                Calc("DEBT_SERVICE", "خدمت بدهی", "ریال", MeasureValueType.Amount, 70, "[PRINCIPAL_REPAYMENT] + [INTEREST_EXPENSE]")
            ]),

        new(
            "OPEX_INFLATION",
            "هزینه عملیاتی مبتنی بر تورم",
            "الگوی ساده برای بودجه هزینه‌های عملیاتی بر مبنای هزینه پایه و نرخ تورم.",
            ["EXPENSE"],
            [
                Assumption("INFLATION_RATE", "نرخ تورم", "%", "نرخ تورم مورد استفاده برای بودجه هزینه‌ها.")
            ],
            [
                Manual("BASE_OPEX", "هزینه پایه", "ریال", MeasureValueType.Amount, 10),
                Calc("INFLATION_ADJUSTMENT", "اثر تورم", "ریال", MeasureValueType.Amount, 20, "[BASE_OPEX] * [ASSUMP:INFLATION_RATE] / 100"),
                Calc("BUDGET_OPEX", "هزینه عملیاتی بودجه‌شده", "ریال", MeasureValueType.Amount, 30, "[BASE_OPEX] + [INFLATION_ADJUSTMENT]")
            ])
    ];

    public static IReadOnlyList<DriverTemplateDto> GetAll() => Templates;

    public static DriverTemplateDto GetRequired(string code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Templates.FirstOrDefault(x => x.Code == normalized)
            ?? throw new KeyNotFoundException($"Driver template '{normalized}' was not found.");
    }

    private static DriverTemplateAssumptionDto Assumption(string code, string name, string? unit, string description) =>
        new(code, name, unit, description);

    private static DriverTemplateMeasureDto Manual(
        string code,
        string name,
        string? unit,
        MeasureValueType valueType,
        int order,
        MeasureAggregation aggregation = MeasureAggregation.Sum) =>
        new(code, name, unit, valueType, aggregation, false, null, order);

    private static DriverTemplateMeasureDto Calc(
        string code,
        string name,
        string? unit,
        MeasureValueType valueType,
        int order,
        string formula,
        MeasureAggregation aggregation = MeasureAggregation.Sum) =>
        new(code, name, unit, valueType, aggregation, true, formula, order);
}
