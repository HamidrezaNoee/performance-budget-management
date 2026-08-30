using PBM.Application;

namespace PBM.Api;

public static class SalesPlanningEndpoints
{
    public static RouteGroupBuilder MapSalesPlanningEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/sales-planning/setup", (Guid companyId, ISalesPlanningService service, CancellationToken ct) =>
            service.GetSetupAsync(companyId, ct));

        api.MapPost("/sales-planning/query", (SalesPlanningQueryRequest request, ISalesPlanningService service, CancellationToken ct) =>
            service.QueryAsync(request, ct));

        api.MapPost("/sales-planning/cell", (UpsertSalesPlanningCellRequest request, ISalesPlanningService service, CancellationToken ct) =>
            service.UpsertCellAsync(request, ct));

        return api;
    }
}
