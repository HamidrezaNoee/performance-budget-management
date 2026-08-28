using PBM.Application;

namespace PBM.Api;

public static class FormulaAdminEndpoints
{
    public static RouteGroupBuilder MapFormulaAdminEndpoints(this RouteGroupBuilder api)
    {
        var formulas = api.MapGroup("/formula-designer");

        formulas.MapGet("/measures", (
            Guid budgetModelId,
            IFormulaAdminService service,
            CancellationToken ct) =>
            service.GetMeasuresAsync(budgetModelId, ct));

        formulas.MapPost("/measures", (
            CreateMeasureDefinitionRequest request,
            IFormulaAdminService service,
            CancellationToken ct) =>
            service.CreateMeasureAsync(request, ct));

        formulas.MapPut("/measures/{measureId:guid}/metadata", (
            Guid measureId,
            UpdateMeasureDefinitionRequest request,
            IFormulaAdminService service,
            CancellationToken ct) =>
            service.UpdateMeasureAsync(measureId, request, ct));

        formulas.MapDelete("/measures/{measureId:guid}", async (
            Guid measureId,
            IFormulaAdminService service,
            CancellationToken ct) =>
        {
            await service.DeleteMeasureAsync(measureId, ct);
            return Results.NoContent();
        });

        formulas.MapPost("/validate", (
            ValidateFormulaRequest request,
            IFormulaAdminService service,
            CancellationToken ct) =>
            service.ValidateAsync(request, ct));

        formulas.MapPut("/measures/{measureId:guid}/formula", (
            Guid measureId,
            UpdateMeasureFormulaRequest request,
            IFormulaAdminService service,
            CancellationToken ct) =>
            service.UpdateFormulaAsync(measureId, request, ct));

        formulas.MapDelete("/measures/{measureId:guid}/formula", (
            Guid measureId,
            IFormulaAdminService service,
            CancellationToken ct) =>
            service.ClearFormulaAsync(measureId, ct));

        return api;
    }
}
