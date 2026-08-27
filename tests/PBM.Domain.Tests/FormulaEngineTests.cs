using PBM.Application;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class FormulaEngineTests
{
    private readonly FormulaEngine _engine = new();

    [Fact]
    public void Calculates_customs_tariff_from_configurable_formula()
    {
        var result = _engine.Evaluate("[CUSTOMS_VALUE] * 0.05", new Dictionary<string, decimal> { ["CUSTOMS_VALUE"] = 10_000_000m });
        Assert.Equal(500_000m, result);
    }

    [Fact]
    public void Calculates_closing_inventory_from_multiple_measures()
    {
        var variables = new Dictionary<string, decimal> { ["OPENING_QTY"] = 100, ["IMPORT_QTY"] = 50, ["SALES_QTY"] = 80, ["FREE_SALES_QTY"] = 5, ["SAMPLE_QTY"] = 2, ["WASTE_QTY"] = 3 };
        var result = _engine.Evaluate("[OPENING_QTY] + [IMPORT_QTY] - [SALES_QTY] - [FREE_SALES_QTY] - [SAMPLE_QTY] - [WASTE_QTY]", variables);
        Assert.Equal(60m, result);
    }

    [Fact]
    public void Supports_safe_functions_without_running_arbitrary_code()
    {
        var result = _engine.Evaluate("ROUND(MAX([A], [B]) / 3, 2)", new Dictionary<string, decimal> { ["A"] = 10, ["B"] = 20 });
        Assert.Equal(6.67m, result);
    }
}
