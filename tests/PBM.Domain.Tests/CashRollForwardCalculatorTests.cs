using PBM.Application;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class CashRollForwardCalculatorTests
{
    [Fact]
    public void Rolls_budget_closing_balance_into_the_next_period()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var result = CashRollForwardCalculator.Calculate("IRR", [
            new CashRollForwardPeriodInput(first, "فروردین", 1, 1_000m, 500m, 200m, null, 0m, 0m, null, null, null, 0m, 100m),
            new CashRollForwardPeriodInput(second, "اردیبهشت", 2, null, 300m, 250m, null, 0m, 0m, null, null, null, 0m, 100m)
        ]);

        Assert.Equal(1_300m, result.Monthly[0].BudgetClosing);
        Assert.Equal(1_300m, result.Monthly[1].BudgetOpening);
        Assert.Equal(1_350m, result.Monthly[1].BudgetClosing);
    }

    [Fact]
    public void Forecast_falls_back_to_budget_when_no_forecast_flow_exists()
    {
        var result = CashRollForwardCalculator.Calculate("IRR", [
            new CashRollForwardPeriodInput(Guid.NewGuid(), "فروردین", 1, 2_000m, 800m, 500m, 1_900m, 700m, 450m, null, null, null, 100m, 300m)
        ]);

        var month = Assert.Single(result.Monthly);
        Assert.Equal(1_900m, month.ForecastOpening);
        Assert.Equal(800m, month.ForecastInflow);
        Assert.Equal(500m, month.ForecastOutflow);
        Assert.Equal(2_200m, month.ForecastClosing);
        Assert.Equal(2_100m, month.ProjectedAvailable);
        Assert.Equal(1_800m, month.LiquidityGap);
    }

    [Fact]
    public void Commitments_and_buffer_surface_a_liquidity_shortfall()
    {
        var result = CashRollForwardCalculator.Calculate("USD", [
            new CashRollForwardPeriodInput(Guid.NewGuid(), "فروردین", 1, 100m, 50m, 40m, null, 0m, 0m, 100m, 20m, 60m, 30m, 50m),
            new CashRollForwardPeriodInput(Guid.NewGuid(), "اردیبهشت", 2, null, 0m, 0m, null, 0m, 0m, null, 10m, 20m, 40m, 50m)
        ]);

        Assert.Equal(2, result.MonthsBelowBuffer);
        Assert.Equal(20m, result.MaximumLiquidityShortfall);
        Assert.Equal(30m, result.ProjectedAvailableEndingCash);
        Assert.Equal(30m, result.MinimumProjectedAvailableCash);
    }

    [Fact]
    public void Explicit_opening_balance_overrides_previous_closing_balance()
    {
        var result = CashRollForwardCalculator.Calculate("IRR", [
            new CashRollForwardPeriodInput(Guid.NewGuid(), "فروردین", 1, 100m, 20m, 10m, null, 0m, 0m, null, null, null, 0m, null),
            new CashRollForwardPeriodInput(Guid.NewGuid(), "اردیبهشت", 2, 500m, 10m, 10m, null, 0m, 0m, 600m, 20m, 10m, 0m, null)
        ]);

        Assert.Equal(500m, result.Monthly[1].BudgetOpening);
        Assert.Equal(600m, result.Monthly[1].ForecastOpening);
    }
}
