using PBM.Application;

namespace PBM.Api;

public static class AssumptionEndpoints
{
    public static RouteGroupBuilder MapAssumptionEndpoints(this RouteGroupBuilder api)
    {
        var assumptions = api.MapGroup("/assumptions");

        assumptions.MapGet("/definitions", (
            bool? includeInactive,
            IAssumptionService service,
            CancellationToken ct) =>
            service.GetDefinitionsAsync(includeInactive ?? false, ct));

        assumptions.MapPost("/definitions", (
            CreateAssumptionDefinitionRequest request,
            IAssumptionService service,
            CancellationToken ct) =>
            service.CreateDefinitionAsync(request, ct));

        assumptions.MapPut("/definitions/{definitionId:guid}", (
            Guid definitionId,
            UpdateAssumptionDefinitionRequest request,
            IAssumptionService service,
            CancellationToken ct) =>
            service.UpdateDefinitionAsync(definitionId, request, ct));

        assumptions.MapGet("/values", (
            Guid companyId,
            Guid fiscalYearId,
            Guid? scenarioId,
            IAssumptionService service,
            CancellationToken ct) =>
            service.GetValuesAsync(companyId, fiscalYearId, scenarioId, ct));

        assumptions.MapPost("/values", (
            UpsertAssumptionValueRequest request,
            IAssumptionService service,
            CancellationToken ct) =>
            service.UpsertValueAsync(request, ct));

        assumptions.MapDelete("/values/{valueId:guid}", async (
            Guid valueId,
            bool? recalculateDraftVersions,
            IAssumptionService service,
            CancellationToken ct) =>
        {
            await service.DeleteValueAsync(valueId, recalculateDraftVersions ?? true, ct);
            return Results.NoContent();
        });

        assumptions.MapGet("/resolved", (
            Guid versionId,
            Guid periodId,
            IAssumptionService service,
            CancellationToken ct) =>
            service.ResolveAsync(versionId, periodId, ct));

        return api;
    }
}
