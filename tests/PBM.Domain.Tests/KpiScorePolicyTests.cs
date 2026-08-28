using PBM.Application;

namespace PBM.Domain.Tests;

public sealed class KpiScorePolicyTests
{
    [Fact]
    public void Higher_is_better_uses_actual_over_target()
    {
        var result = KpiScorePolicy.Evaluate(target: 100m, actual: 120m, minimum: 80m, maximum: null);

        Assert.Equal(KpiScoreMode.HigherIsBetter, result.Mode);
        Assert.Equal(120m, result.Score);
        Assert.True(result.IsOnTarget);
    }

    [Fact]
    public void Lower_is_better_uses_target_over_actual()
    {
        var result = KpiScorePolicy.Evaluate(target: 10m, actual: 8m, minimum: null, maximum: 12m);

        Assert.Equal(KpiScoreMode.LowerIsBetter, result.Mode);
        Assert.Equal(125m, result.Score);
        Assert.True(result.IsOnTarget);
    }

    [Fact]
    public void Lower_is_better_penalizes_value_above_target()
    {
        var result = KpiScorePolicy.Evaluate(target: 10m, actual: 20m, minimum: null, maximum: 15m);

        Assert.Equal(50m, result.Score);
        Assert.False(result.IsOnTarget);
    }

    [Fact]
    public void Target_range_returns_full_score_inside_band()
    {
        var result = KpiScorePolicy.Evaluate(target: 0m, actual: 95m, minimum: 90m, maximum: 100m);

        Assert.Equal(KpiScoreMode.TargetRange, result.Mode);
        Assert.Equal(100m, result.Score);
        Assert.True(result.IsOnTarget);
    }

    [Fact]
    public void Target_range_penalizes_values_outside_band()
    {
        var result = KpiScorePolicy.Evaluate(target: 0m, actual: 120m, minimum: 90m, maximum: 100m);

        Assert.Equal(83.33m, result.Score);
        Assert.False(result.IsOnTarget);
    }

    [Fact]
    public void Score_is_capped_to_avoid_single_kpi_domination()
    {
        var result = KpiScorePolicy.Evaluate(target: 10m, actual: 1m, minimum: null, maximum: 10m);

        Assert.Equal(KpiScorePolicy.DefaultScoreCap, result.Score);
    }

    [Fact]
    public void Invalid_target_range_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            KpiScorePolicy.Evaluate(target: 100m, actual: 100m, minimum: 120m, maximum: 80m));
    }
}
