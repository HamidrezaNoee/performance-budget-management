namespace PBM.Application;

public static class BudgetSpreadCalculator
{
    public static IReadOnlyList<decimal> Spread(decimal total, int periodCount, IReadOnlyList<decimal>? weights = null)
    {
        if (periodCount <= 0) throw new ArgumentOutOfRangeException(nameof(periodCount), "Period count must be greater than zero.");
        if (weights is not null && weights.Count != periodCount)
            throw new ArgumentException("Weight count must match period count.", nameof(weights));

        var effectiveWeights = weights ?? Enumerable.Repeat(1m, periodCount).ToArray();
        if (effectiveWeights.Any(x => x < 0m)) throw new ArgumentException("Weights cannot be negative.", nameof(weights));
        var totalWeight = effectiveWeights.Sum();
        if (totalWeight <= 0m) throw new ArgumentException("At least one spread weight must be greater than zero.", nameof(weights));

        var result = new decimal[periodCount];
        decimal allocated = 0m;
        for (var index = 0; index < periodCount; index++)
        {
            var value = index == periodCount - 1
                ? total - allocated
                : Math.Round(total * effectiveWeights[index] / totalWeight, 8, MidpointRounding.AwayFromZero);
            result[index] = value;
            allocated += value;
        }

        return result;
    }
}
