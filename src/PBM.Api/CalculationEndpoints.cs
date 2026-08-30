using PBM.Application;

namespace PBM.Api;

public static class CalculationEndpoints
{
    public static RouteGroupBuilder MapCalculationEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/calculations/versions/{versionId:guid}/recalculate", (Guid versionId, ICalculationService service, CancellationToken ct) =>
            service.RecalculateVersionAsync(versionId, ct));
        return api;
    }
}
