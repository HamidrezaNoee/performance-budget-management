using PBM.Application;

namespace PBM.Api;

public static class ScenarioEndpoints
{
    public static RouteGroupBuilder MapScenarioEndpoints(this RouteGroupBuilder api)
    {
        var scenarios = api.MapGroup("/scenarios");
        scenarios.MapGet("/", (IScenarioService service, CancellationToken ct) => service.GetAsync(ct));
        scenarios.MapPost("/", (CreateBudgetScenarioRequest request, IScenarioService service, CancellationToken ct) => service.CreateAsync(request, ct));
        scenarios.MapPut("/{scenarioId:guid}", (Guid scenarioId, UpdateBudgetScenarioRequest request, IScenarioService service, CancellationToken ct) => service.UpdateAsync(scenarioId, request, ct));
        return api;
    }
}
