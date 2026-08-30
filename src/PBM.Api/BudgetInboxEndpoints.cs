using PBM.Application;

namespace PBM.Api;

public static class BudgetInboxEndpoints
{
    public static RouteGroupBuilder MapBudgetInboxEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/workflow/inbox", (Guid? companyId, IBudgetInboxService service, CancellationToken ct) =>
            service.GetInboxAsync(companyId, ct));
        return api;
    }
}
