using PBM.Application;

namespace PBM.Api;

public static class PerformanceBudgetingEndpoints
{
    public static RouteGroupBuilder MapPerformanceBudgetingEndpoints(this RouteGroupBuilder api)
    {
        var performance = api.MapGroup("/performance-budgeting");
        performance.MapGet("/scorecard", (
            Guid versionId,
            IPerformanceBudgetingService service,
            CancellationToken ct) =>
            service.GetScorecardAsync(versionId, ct));
        return api;
    }
}
