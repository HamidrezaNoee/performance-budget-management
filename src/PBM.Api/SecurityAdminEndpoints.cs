using PBM.Application;

namespace PBM.Api;

public static class SecurityAdminEndpoints
{
    public static RouteGroupBuilder MapSecurityAdminEndpoints(this RouteGroupBuilder api)
    {
        var security = api.MapGroup("/admin/security");

        security.MapGet("/users", (ISecurityAdminService service, CancellationToken ct) => service.GetUsersAsync(ct));
        security.MapGet("/roles", (ISecurityAdminService service, CancellationToken ct) => service.GetRolesAsync(ct));
        security.MapGet("/license-usage", (ISecurityAdminService service, CancellationToken ct) => service.GetLicenseUsageAsync(ct));

        security.MapPost("/users", (CreateSecurityUserRequest request, ISecurityAdminService service, CancellationToken ct) =>
            service.CreateUserAsync(request, ct));

        security.MapPut("/users/{userId:guid}", (Guid userId, UpdateSecurityUserRequest request, ISecurityAdminService service, CancellationToken ct) =>
            service.UpdateUserAsync(userId, request, ct));

        security.MapPut("/users/{userId:guid}/password", async (Guid userId, ChangeUserPasswordRequest request, ISecurityAdminService service, CancellationToken ct) =>
        {
            await service.ChangePasswordAsync(userId, request.NewPassword, ct);
            return Results.NoContent();
        });

        security.MapPost("/roles", (CreateSecurityRoleRequest request, ISecurityAdminService service, CancellationToken ct) =>
            service.CreateRoleAsync(request, ct));

        return api;
    }
}
