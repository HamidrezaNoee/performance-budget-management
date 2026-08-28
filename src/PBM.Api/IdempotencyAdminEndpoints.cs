using PBM.Application;
using PBM.Domain;

namespace PBM.Api;

public static class IdempotencyAdminEndpoints
{
    public static RouteGroupBuilder MapIdempotencyAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/idempotency");

        admin.MapGet("/", (
            IdempotencyRecordStatus? status,
            int? take,
            IIdempotencyAdminService service,
            CancellationToken ct) =>
            service.GetAsync(status, take ?? 200, ct));

        admin.MapPost("/{recordId:guid}/resolve", async (
            Guid recordId,
            ResolveIdempotencyRequest request,
            IIdempotencyAdminService service,
            CancellationToken ct) =>
        {
            await service.ResolveAsync(recordId, request, ct);
            return Results.NoContent();
        });

        admin.MapDelete("/expired-completed", async (
            int? take,
            IIdempotencyAdminService service,
            CancellationToken ct) =>
        {
            var removed = await service.CleanupExpiredCompletedAsync(take ?? 1000, ct);
            return Results.Ok(new { removed });
        });

        return api;
    }
}
