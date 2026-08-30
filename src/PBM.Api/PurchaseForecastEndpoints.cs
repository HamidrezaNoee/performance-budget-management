using PBM.Application;

namespace PBM.Api;

public static class PurchaseForecastEndpoints
{
    public static RouteGroupBuilder MapPurchaseForecastEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/purchase-forecast").WithTags("Purchase Forecast");

        group.MapGet("/setup", (
            Guid companyId,
            IPurchaseForecastService service,
            CancellationToken ct) => service.GetSetupAsync(companyId, ct));

        group.MapPost("/cost-types", (
            CreatePurchaseCostTypeRequest request,
            IPurchaseForecastService service,
            CancellationToken ct) => service.CreateCostTypeAsync(request, ct));

        group.MapPost("/query", (
            PurchaseForecastQueryRequest request,
            IPurchaseForecastService service,
            CancellationToken ct) => service.QueryAsync(request, ct));

        group.MapPost("/cell", async (
            UpsertPurchaseForecastCellRequest request,
            IPurchaseForecastService service,
            CancellationToken ct) => Results.Ok(new { id = await service.UpsertCellAsync(request, ct) }));

        return api;
    }
}
