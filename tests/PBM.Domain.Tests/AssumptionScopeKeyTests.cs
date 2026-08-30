using PBM.Domain;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class AssumptionScopeKeyTests
{
    [Fact]
    public void Global_annual_scope_is_stable()
    {
        Assert.Equal("S:GLOBAL|P:ANNUAL", AssumptionScopeKey.Create(null, null));
    }

    [Fact]
    public void Scenario_and_period_are_both_part_of_the_scope_key()
    {
        var scenarioId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var periodId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var key = AssumptionScopeKey.Create(scenarioId, periodId);

        Assert.Equal("S:11111111111111111111111111111111|P:22222222222222222222222222222222", key);
    }

    [Fact]
    public void Annual_and_period_specific_scopes_do_not_collide()
    {
        var periodId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        Assert.NotEqual(AssumptionScopeKey.Create(null, null), AssumptionScopeKey.Create(null, periodId));
    }
}
