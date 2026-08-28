using PBM.Application;

namespace PBM.Domain.Tests;

public sealed class StrategicScorePolicyTests
{
    [Fact]
    public void Positive_weights_are_normalized_for_score_and_coverage()
    {
        var result = StrategicScorePolicy.Aggregate([
            new WeightedScoreInput(60m, 100m),
            new WeightedScoreInput(40m, 50m)
        ]);

        Assert.Equal(100m, result.CoveragePercent);
        Assert.Equal(80m, result.WeightedScore);
        Assert.Equal(100m, result.TotalWeight);
        Assert.Equal(100m, result.ObservedWeight);
    }

    [Fact]
    public void Missing_weighted_observation_reduces_coverage_but_not_observed_average()
    {
        var result = StrategicScorePolicy.Aggregate([
            new WeightedScoreInput(70m, 90m),
            new WeightedScoreInput(30m, null)
        ]);

        Assert.Equal(70m, result.CoveragePercent);
        Assert.Equal(90m, result.WeightedScore);
        Assert.Equal(1, result.ObservedCount);
    }

    [Fact]
    public void Zero_weights_fall_back_to_equal_average_and_count_coverage()
    {
        var result = StrategicScorePolicy.Aggregate([
            new WeightedScoreInput(0m, 80m),
            new WeightedScoreInput(0m, 100m),
            new WeightedScoreInput(0m, null)
        ]);

        Assert.Equal(66.67m, result.CoveragePercent);
        Assert.Equal(90m, result.WeightedScore);
    }

    [Fact]
    public void Empty_input_returns_zero_coverage_and_no_score()
    {
        var result = StrategicScorePolicy.Aggregate([]);

        Assert.Equal(0m, result.CoveragePercent);
        Assert.Null(result.WeightedScore);
    }

    [Fact]
    public void Negative_weight_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            StrategicScorePolicy.Aggregate([new WeightedScoreInput(-1m, 100m)]));
    }
}
