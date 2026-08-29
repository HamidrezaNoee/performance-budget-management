using Microsoft.EntityFrameworkCore;
using PBM.Domain;
using Xunit;

namespace PBM.Integration.Tests;

[Collection("PBM SQL Integration")]
public sealed class TradePlanningSqlTests(PbmSqlFixture fixture)
{
    [Fact]
    public async Task Trade_seed_contains_origin_to_warehouse_inventory_and_sales_measures()
    {
        if (!fixture.IsEnabled) return;

        await using var db = fixture.CreateContext();
        var trade = await db.BudgetModels.AsNoTracking()
            .SingleAsync(x => x.TenantId == fixture.TenantId && x.Code == "TRADE");

        var measures = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == trade.Id)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);

        var requiredCodes = new[]
        {
            "CPT_UNIT_PRICE", "FX_RATE", "IMPORT_QTY", "IMPORT_FX", "PURCHASE_IRR_AMOUNT",
            "ORDER_REG_RATE", "ORDER_REG_FEE_CALC", "BANK_FEE_RATE", "BANK_FEE_CALC",
            "INSURANCE_RATE", "INSURANCE_CALC", "CUSTOMS_TARIFF_RATE", "CUSTOMS_DUTY_CALC",
            "VAT_RATE", "VAT_AMOUNT", "FREIGHT_IRR", "CLEARANCE_FEE", "INLAND_TRANSPORT",
            "TRADE_LANDED_COST_TOTAL", "TRADE_LANDED_COST_PER_UNIT",
            "OPENING_QTY", "OPENING_VALUE", "AVAILABLE_QTY", "COGS_QTY", "COGS_AMOUNT",
            "FREE_SALES_QTY", "FOC_COST", "SAMPLE_QTY", "SAMPLE_AMOUNT", "WASTE_QTY",
            "WASTE_AMOUNT", "TOTAL_COGS_AMOUNT", "CLOSING_QTY", "CLOSING_VALUE",
            "SALES_QTY", "SALES_PRICE", "GROSS_SALES", "SALES_DISCOUNT", "NET_SALES",
            "TRADE_GROSS_MARGIN", "TRADE_GROSS_MARGIN_PERCENT"
        };

        foreach (var code in requiredCodes)
            Assert.True(measures.ContainsKey(code), $"TRADE measure '{code}' was not seeded.");

        Assert.Equal("[IMPORT_FX] * [FX_RATE]", measures["PURCHASE_IRR_AMOUNT"].FormulaExpression);
        Assert.Equal(
            "[PURCHASE_IRR_AMOUNT] + [ORDER_REG_FEE_CALC] + [BANK_FEE_CALC] + [INSURANCE_CALC] + [CUSTOMS_DUTY_CALC] + [VAT_AMOUNT] + [FREIGHT_IRR] + [CLEARANCE_FEE] + [INLAND_TRANSPORT] + [OTHER_IMPORT_COST]",
            measures["TRADE_LANDED_COST_TOTAL"].FormulaExpression);
        Assert.Equal("[OPENING_QTY] + [IMPORT_QTY]", measures["AVAILABLE_QTY"].FormulaExpression);
        Assert.Equal("[COGS_AMOUNT] + [FOC_COST] + [SAMPLE_AMOUNT] + [WASTE_AMOUNT]", measures["TOTAL_COGS_AMOUNT"].FormulaExpression);
        Assert.Equal("[GROSS_SALES] - [SALES_DISCOUNT]", measures["NET_SALES"].FormulaExpression);
        Assert.Equal("[NET_SALES] - [TOTAL_COGS_AMOUNT]", measures["TRADE_GROSS_MARGIN"].FormulaExpression);
        Assert.Equal(MeasureAggregation.LastNonEmpty, measures["OPENING_VALUE"].Aggregation);
        Assert.Equal(MeasureAggregation.LastNonEmpty, measures["CLOSING_VALUE"].Aggregation);
    }
}
