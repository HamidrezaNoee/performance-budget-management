using PBM.Application;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class ReservationReconciliationPolicyTests
{
    [Fact]
    public void Missing_actual_inside_grace_period_is_awaiting()
    {
        var result = ReservationReconciliationPolicy.Evaluate(100m, null, 1, 2, 0.1m, false);
        Assert.Equal(ReservationReconciliationStatus.AwaitingActual, result.Status);
    }

    [Fact]
    public void Missing_actual_after_grace_period_is_flagged()
    {
        var result = ReservationReconciliationPolicy.Evaluate(100m, null, 3, 2, 0.1m, false);
        Assert.Equal(ReservationReconciliationStatus.MissingActual, result.Status);
    }

    [Fact]
    public void Small_variance_inside_relative_tolerance_is_reconciled()
    {
        var result = ReservationReconciliationPolicy.Evaluate(1_000m, 1_000.5m, 5, 2, 0.1m, false);
        Assert.Equal(ReservationReconciliationStatus.Reconciled, result.Status);
        Assert.Equal(1m, result.AllowedTolerance);
    }

    [Fact]
    public void Lower_actual_is_under_posted()
    {
        var result = ReservationReconciliationPolicy.Evaluate(1_000m, 900m, 5, 2, 0.1m, false);
        Assert.Equal(ReservationReconciliationStatus.UnderPosted, result.Status);
        Assert.Equal(-100m, result.Variance);
    }

    [Fact]
    public void Higher_actual_is_over_posted()
    {
        var result = ReservationReconciliationPolicy.Evaluate(1_000m, 1_200m, 5, 2, 0.1m, false);
        Assert.Equal(ReservationReconciliationStatus.OverPosted, result.Status);
        Assert.Equal(200m, result.Variance);
    }

    [Fact]
    public void Currency_conflict_has_highest_priority()
    {
        var result = ReservationReconciliationPolicy.Evaluate(1_000m, 1_000m, 0, 2, 0.1m, true);
        Assert.Equal(ReservationReconciliationStatus.CurrencyConflict, result.Status);
    }
}
