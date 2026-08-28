using PBM.Application;
using PBM.Domain;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class BudgetFactWritePolicyTests
{
    [Theory]
    [InlineData(ValueKind.Budget)]
    [InlineData(ValueKind.Actual)]
    [InlineData(ValueKind.Commitment)]
    [InlineData(ValueKind.Forecast)]
    public void Unlocked_draft_accepts_all_fact_kinds(ValueKind kind)
    {
        var result = BudgetFactWritePolicy.Evaluate(BudgetStatus.Draft, false, kind);
        Assert.True(result.IsAllowed);
        Assert.Null(result.DenialReason);
    }

    [Theory]
    [InlineData(ValueKind.Actual)]
    [InlineData(ValueKind.Commitment)]
    public void Approved_version_accepts_execution_fact_kinds(ValueKind kind)
    {
        var result = BudgetFactWritePolicy.Evaluate(BudgetStatus.Approved, true, kind);
        Assert.True(result.IsAllowed);
    }

    [Theory]
    [InlineData(ValueKind.Budget)]
    [InlineData(ValueKind.Forecast)]
    public void Approved_version_rejects_planning_fact_kinds(ValueKind kind)
    {
        var result = BudgetFactWritePolicy.Evaluate(BudgetStatus.Approved, true, kind);
        Assert.False(result.IsAllowed);
        Assert.Contains("revision", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(BudgetStatus.Submitted)]
    [InlineData(BudgetStatus.UnderReview)]
    [InlineData(BudgetStatus.Returned)]
    [InlineData(BudgetStatus.Rejected)]
    [InlineData(BudgetStatus.Revised)]
    [InlineData(BudgetStatus.Closed)]
    public void Non_editable_workflow_states_reject_execution_writes(BudgetStatus status)
    {
        var result = BudgetFactWritePolicy.Evaluate(status, true, ValueKind.Actual);
        Assert.False(result.IsAllowed);
        Assert.False(string.IsNullOrWhiteSpace(result.DenialReason));
    }

    [Fact]
    public void Locked_draft_is_rejected()
    {
        var result = BudgetFactWritePolicy.Evaluate(BudgetStatus.Draft, true, ValueKind.Budget);
        Assert.False(result.IsAllowed);
    }
}
