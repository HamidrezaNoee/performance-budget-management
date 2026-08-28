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

        formulas.MapPost("/validate", (
            ValidateFormulaRequest request,
            IFormulaAdminService service,
            CancellationToken ct) =>
            service.ValidateAsync(request, ct));

        formulas.MapPut("/measures/{measureId:guid}", (
            Guid measureId,
            UpdateMeasureFormulaRequest request,
            IFormulaAdminService service,
            CancellationToken ct) =>
            service.UpdateFormulaAsync(measureId, request, ct));

        formulas.MapDelete("/measures/{measureId:guid}", (
            Guid measureId,
            IFormulaAdminService service,
            CancellationToken ct) =>
            service.ClearFormulaAsync(measureId, ct));

        return api;
    }
}
