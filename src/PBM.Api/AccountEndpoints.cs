using PBM.Application;

namespace PBM.Api;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var account = api.MapGroup("/account");
        account.MapPost("/change-password", async (ChangeOwnPasswordRequest request, IAccountService service, CancellationToken ct) =>
        {
            await service.ChangePasswordAsync(request, ct);
            return Results.NoContent();
        });
        return api;
    }
}
