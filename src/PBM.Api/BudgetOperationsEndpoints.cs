using PBM.Application;

namespace PBM.Api;

public static class BudgetOperationsEndpoints
{
    public static RouteGroupBuilder MapBudgetOperationsEndpoints(this RouteGroupBuilder api)
    {
        var operations = api.MapGroup("/budget/operations");

        operations.MapPost("/copy-prior-year-actual", (CopyPriorYearActualRequest request, IBudgetOperationsService service, CancellationToken ct) =>
            service.CopyPriorYearActualAsync(request, ct));

        operations.MapPost("/spread", (SpreadBudgetRequest request, IBudgetOperationsService service, CancellationToken ct) =>
            service.SpreadAsync(request, ct));

        operations.MapPost("/bulk-paste", (BulkBudgetPasteRequest request, IBudgetOperationsService service, CancellationToken ct) =>
            service.BulkPasteAsync(request, ct));

        operations.MapPost("/compare", (BudgetVersionComparisonQuery request, IBudgetOperationsService service, CancellationToken ct) =>
            service.CompareVersionsAsync(request, ct));

        return api;
    }
}
