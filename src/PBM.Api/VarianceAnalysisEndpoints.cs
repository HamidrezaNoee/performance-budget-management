using PBM.Application;

namespace PBM.Api;

public static class VarianceAnalysisEndpoints
{
    public static RouteGroupBuilder MapVarianceAnalysisEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/analytics/variance", (VarianceAnalysisQuery request, IVarianceAnalysisService service, CancellationToken ct) =>
            service.AnalyzeAsync(request, ct));
        return api;
    }
}
