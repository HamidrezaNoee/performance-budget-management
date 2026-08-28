using PBM.Application;

namespace PBM.Domain.Tests;

public sealed class PerformanceFundingPolicyTests
{
    [Fact]
    public void Insufficient_data_wins_when_coverage_is_low()
    {
        var result = PerformanceFundingPolicy.Evaluate(40m, 120m, []);

        Assert.Equal(PerformanceFundingRecommendation.InsufficientData, result.Recommendation);
    }

    [Fact]
    public void Material_ytd_overrun_requires_corrective_action()
    {
        var result = PerformanceFundingPolicy.Evaluate(
            100m,
            120m,
            [new PerformanceFundingCurrencySignal("IRR", 1000m, 0m, 0m, 900m, 500m, 500m, 100m, 450m)]);

        Assert.Equal(PerformanceFundingRecommendation.CorrectiveAction, result.Recommendation);
    }

    [Fact]
    public void Low_kpi_score_triggers_funding_review()
    {
        var result = PerformanceFundingPolicy.Evaluate(
            100m,
            60m,
            [new PerformanceFundingCurrencySignal("IRR", 1000m, 500m, 0m, 900m, 500m, 250m, 0m, 450m)]);

        Assert.Equal(PerformanceFundingRecommendation.ReviewFunding, result.Recommendation);
    }

    [Fact]
    public void Exceptional_performance_without_overrun_is_priority_for_increment()
    {
        var result = PerformanceFundingPolicy.Evaluate(
            100m,
            115m,
            [new PerformanceFundingCurrencySignal("IRR", 1000m, 700m, 50m, 1000m, 750m, 600m, 50m, 700m)]);

        Assert.Equal(PerformanceFundingRecommendation.PriorityForIncrement, result.Recommendation);
    }

    [Fact]
    public void Healthy_performance_maintains_funding()
    {
        var result = PerformanceFundingPolicy.Evaluate(
            100m,
            96m,
            [new PerformanceFundingCurrencySignal("IRR", 1000m, 650m, 100m, 950m, 750m, 550m, 75m, 700m)]);

        Assert.Equal(PerformanceFundingRecommendation.MaintainFunding, result.Recommendation);
    }

    [Fact]
    public void Zero_budget_with_actual_is_corrective_action()
    {
        var result = PerformanceFundingPolicy.Evaluate(
            100m,
            100m,
            [new PerformanceFundingCurrencySignal("USD", 0m, 10m, 0m, 0m, 0m, 10m, 0m, 0m)]);

        Assert.Equal(PerformanceFundingRecommendation.CorrectiveAction, result.Recommendation);
    }
}
