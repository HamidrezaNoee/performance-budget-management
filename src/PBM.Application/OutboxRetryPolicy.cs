using PBM.Domain;

namespace PBM.Application;

public sealed record OutboxRetryDecision(OutboxStatus Status, DateTime NextAttemptAtUtc);

public static class OutboxRetryPolicy
{
    public static OutboxRetryDecision Evaluate(
        int attempts,
        int maxAttempts,
        int baseDelaySeconds,
        int maxDelaySeconds,
        DateTime utcNow)
    {
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (baseDelaySeconds < 1) throw new ArgumentOutOfRangeException(nameof(baseDelaySeconds));
        if (maxDelaySeconds < baseDelaySeconds) throw new ArgumentOutOfRangeException(nameof(maxDelaySeconds));

        if (attempts >= maxAttempts)
            return new OutboxRetryDecision(OutboxStatus.DeadLetter, utcNow);

        var multiplier = Math.Pow(2, Math.Min(attempts - 1, 16));
        var delaySeconds = Math.Min(maxDelaySeconds, baseDelaySeconds * multiplier);
        return new OutboxRetryDecision(OutboxStatus.Pending, utcNow.AddSeconds(delaySeconds));
    }
}
