using PBM.Application;
using PBM.Domain;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class OutboxRetryPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(4, 120)]
    public void Retry_delay_grows_exponentially(int attempts, int expectedDelaySeconds)
    {
        var decision = OutboxRetryPolicy.Evaluate(attempts, 8, 15, 3600, Now);

        Assert.Equal(OutboxStatus.Pending, decision.Status);
        Assert.Equal(Now.AddSeconds(expectedDelaySeconds), decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Retry_delay_is_capped()
    {
        var decision = OutboxRetryPolicy.Evaluate(10, 20, 15, 300, Now);

        Assert.Equal(OutboxStatus.Pending, decision.Status);
        Assert.Equal(Now.AddSeconds(300), decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Final_attempt_goes_to_dead_letter_without_future_delay()
    {
        var decision = OutboxRetryPolicy.Evaluate(8, 8, 15, 3600, Now);

        Assert.Equal(OutboxStatus.DeadLetter, decision.Status);
        Assert.Equal(Now, decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Invalid_policy_configuration_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboxRetryPolicy.Evaluate(0, 8, 15, 3600, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboxRetryPolicy.Evaluate(1, 0, 15, 3600, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboxRetryPolicy.Evaluate(1, 8, 0, 3600, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboxRetryPolicy.Evaluate(1, 8, 60, 30, Now));
    }
}
