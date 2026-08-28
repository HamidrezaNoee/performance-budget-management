using PBM.Application;

namespace PBM.Domain.Tests;

public sealed class BudgetSpreadCalculatorTests
{
    [Fact]
    public void Spread_evenly_preserves_total_and_assigns_rounding_residual_to_last_period()
    {
        var values = BudgetSpreadCalculator.Spread(100m, 3);

        Assert.Equal(new[] { 33.33333333m, 33.33333333m, 33.33333334m }, values);
        Assert.Equal(100m, values.Sum());
    }

    [Fact]
    public void Spread_weighted_uses_requested_weights()
    {
        var values = BudgetSpreadCalculator.Spread(1200m, 3, new[] { 1m, 2m, 3m });

        Assert.Equal(new[] { 200m, 400m, 600m }, values);
        Assert.Equal(1200m, values.Sum());
    }

    [Fact]
    public void Spread_supports_negative_totals()
    {
        var values = BudgetSpreadCalculator.Spread(-90m, 3);

        Assert.Equal(new[] { -30m, -30m, -30m }, values);
    }

    [Fact]
    public void Spread_rejects_invalid_weights()
    {
        Assert.Throws<ArgumentException>(() => BudgetSpreadCalculator.Spread(100m, 2, new[] { 1m }));
        Assert.Throws<ArgumentException>(() => BudgetSpreadCalculator.Spread(100m, 2, new[] { 1m, -1m }));
        Assert.Throws<ArgumentException>(() => BudgetSpreadCalculator.Spread(100m, 2, new[] { 0m, 0m }));
    }
}
