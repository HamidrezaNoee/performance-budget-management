using PBM.Application;

namespace PBM.Api;

public static class StrategyEndpoints
{
    public static RouteGroupBuilder MapStrategyEndpoints(this RouteGroupBuilder api)
    {
        var strategy = api.MapGroup("/strategy");

        strategy.MapGet("/objectives", (
            bool? includeInactive,
            IStrategyService service,
            CancellationToken ct) =>
            service.GetObjectivesAsync(includeInactive ?? false, ct));

        strategy.MapPost("/objectives", (
            CreateStrategicObjectiveRequest request,
            IStrategyService service,
            CancellationToken ct) =>
            service.CreateObjectiveAsync(request, ct));

        strategy.MapPut("/objectives/{objectiveId:guid}", (
            Guid objectiveId,
            UpdateStrategicObjectiveRequest request,
            IStrategyService service,
            CancellationToken ct) =>
            service.UpdateObjectiveAsync(objectiveId, request, ct));

        strategy.MapGet("/kpi-objective-links", (
            IStrategyService service,
            CancellationToken ct) =>
            service.GetKpiLinksAsync(ct));

        strategy.MapPut("/kpi-objective-links", (
            UpsertKpiObjectiveLinkRequest request,
            IStrategyService service,
            CancellationToken ct) =>
            service.UpsertKpiLinkAsync(request, ct));

        strategy.MapDelete("/kpi-objective-links/{kpiId:guid}/{objectiveId:guid}", async (
            Guid kpiId,
            Guid objectiveId,
            IStrategyService service,
            CancellationToken ct) =>
        {
            await service.DeleteKpiLinkAsync(kpiId, objectiveId, ct);
            return Results.NoContent();
        });

        return api;
    }
}
