using PBM.Application;

namespace PBM.Api;

public static class ExpensePlanningEndpoints
{
    public static RouteGroupBuilder MapExpensePlanningEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/expense-planning/setup", (Guid companyId, IExpensePlanningService service, CancellationToken ct) =>
            service.GetSetupAsync(companyId, ct));
        api.MapPost("/expense-planning/query", (ExpensePlanningQueryRequest request, IExpensePlanningService service, CancellationToken ct) =>
            service.QueryAsync(request, ct));
        api.MapPost("/expense-planning/cell", (UpsertExpensePlanningCellRequest request, IExpensePlanningService service, CancellationToken ct) =>
            service.UpsertCellAsync(request, ct));
        api.MapPost("/expense-planning/items", (CreateExpenseItemRequest request, IExpensePlanningService service, CancellationToken ct) =>
            service.CreateItemAsync(request, ct));
        return api;
    }
}
