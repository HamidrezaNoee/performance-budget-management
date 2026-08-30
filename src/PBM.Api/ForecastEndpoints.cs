using PBM.Application;

namespace PBM.Api;

public static class ForecastEndpoints
{
    public static RouteGroupBuilder MapForecastEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/forecast", (Guid companyId, Guid fiscalYearId, Guid measureId, ForecastMethod? method, IForecastService service, CancellationToken ct) =>
            service.GenerateAsync(companyId, fiscalYearId, measureId, method ?? ForecastMethod.LinearTrend, ct));
        return api;
    }
}
