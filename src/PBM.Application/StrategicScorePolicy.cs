namespace PBM.Application;

public sealed record WeightedScoreInput(decimal Weight, decimal? Score);

public sealed record WeightedScoreResult(
    decimal CoveragePercent,
    decimal? WeightedScore,
    decimal TotalWeight,
    decimal ObservedWeight,
    int TotalCount,
    int ObservedCount);

public static class StrategicScorePolicy
{
    public static WeightedScoreResult Aggregate(IReadOnlyList<WeightedScoreInput> inputs)
    {
        if (inputs.Any(x => x.Weight < 0m))
            throw new ArgumentException("Strategic score weights cannot be negative.");

        var totalCount = inputs.Count;
        var observed = inputs.Where(x => x.Score.HasValue).ToList();
        var observedCount = observed.Count;
        var totalWeight = inputs.Where(x => x.Weight > 0m).Sum(x => x.Weight);
        var observedWeight = observed.Where(x => x.Weight > 0m).Sum(x => x.Weight);

        var coverage = totalWeight > 0m
            ? Percent(observedWeight, totalWeight)
            : totalCount == 0
                ? 0m
                : Percent(observedCount, totalCount);

        decimal? score = null;
        if (observedCount > 0)
        {
            score = observedWeight > 0m
                ? Round(observed.Where(x => x.Weight > 0m).Sum(x => x.Score!.Value * x.Weight) / observedWeight)
                : Round(observed.Average(x => x.Score!.Value));
        }

        return new WeightedScoreResult(
            coverage,
            score,
            totalWeight,
            observedWeight,
            totalCount,
            observedCount);
    }

    private static decimal Percent(decimal numerator, decimal denominator) =>
        denominator == 0m ? 0m : Round(numerator / denominator * 100m);

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
