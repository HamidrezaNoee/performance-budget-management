namespace PBM.Application;

public enum KpiScoreMode
{
    HigherIsBetter,
    LowerIsBetter,
    TargetRange
}

public sealed record KpiScoreResult(
    KpiScoreMode Mode,
    decimal Score,
    decimal ReferenceTarget,
    bool IsOnTarget);

public static class KpiScorePolicy
{
    public const decimal DefaultScoreCap = 150m;

    public static KpiScoreResult Evaluate(
        decimal target,
        decimal actual,
        decimal? minimum,
        decimal? maximum,
        decimal scoreCap = DefaultScoreCap)
    {
        if (scoreCap < 100m || scoreCap > 1000m)
            throw new ArgumentOutOfRangeException(nameof(scoreCap), "KPI score cap must be between 100 and 1000.");
        if (minimum.HasValue && maximum.HasValue && minimum.Value > maximum.Value)
            throw new ArgumentException("KPI minimum cannot be greater than maximum.");

        if (minimum.HasValue && maximum.HasValue)
            return EvaluateRange(actual, minimum.Value, maximum.Value, scoreCap);

        if (maximum.HasValue && !minimum.HasValue)
        {
            var reference = ResolvePositiveReference(target, maximum.Value);
            if (reference <= 0m)
                return new KpiScoreResult(KpiScoreMode.LowerIsBetter, actual <= 0m ? 100m : 0m, reference, actual <= 0m);

            if (actual <= 0m)
                return new KpiScoreResult(KpiScoreMode.LowerIsBetter, scoreCap, reference, true);

            var score = Clamp(reference / actual * 100m, 0m, scoreCap);
            return new KpiScoreResult(KpiScoreMode.LowerIsBetter, Round(score), reference, actual <= reference);
        }

        var higherReference = ResolvePositiveReference(target, minimum ?? 0m);
        if (higherReference <= 0m)
        {
            var onTarget = actual >= 0m;
            return new KpiScoreResult(KpiScoreMode.HigherIsBetter, onTarget ? 100m : 0m, higherReference, onTarget);
        }

        var higherScore = Clamp(actual / higherReference * 100m, 0m, scoreCap);
        return new KpiScoreResult(KpiScoreMode.HigherIsBetter, Round(higherScore), higherReference, actual >= higherReference);
    }

    private static KpiScoreResult EvaluateRange(decimal actual, decimal minimum, decimal maximum, decimal scoreCap)
    {
        if (actual >= minimum && actual <= maximum)
            return new KpiScoreResult(KpiScoreMode.TargetRange, 100m, (minimum + maximum) / 2m, true);

        decimal score;
        decimal reference;
        if (actual < minimum)
        {
            reference = minimum;
            score = minimum <= 0m
                ? (actual >= minimum ? 100m : 0m)
                : actual / minimum * 100m;
        }
        else
        {
            reference = maximum;
            score = actual <= 0m || maximum <= 0m
                ? 0m
                : maximum / actual * 100m;
        }

        return new KpiScoreResult(
            KpiScoreMode.TargetRange,
            Round(Clamp(score, 0m, scoreCap)),
            reference,
            false);
    }

    private static decimal ResolvePositiveReference(decimal target, decimal fallback) =>
        target > 0m ? target : fallback;

    private static decimal Clamp(decimal value, decimal minimum, decimal maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
