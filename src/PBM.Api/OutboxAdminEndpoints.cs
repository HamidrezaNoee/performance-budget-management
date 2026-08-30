using PBM.Application;
using PBM.Domain;

namespace PBM.Api;

public static class OutboxAdminEndpoints
{
    public static RouteGroupBuilder MapOutboxAdminEndpoints(this RouteGroupBuilder api)
    {
        var outbox = api.MapGroup("/operations/outbox");
        outbox.MapGet("/summary", (IOutboxAdminService service, CancellationToken ct) =>
            service.GetSummaryAsync(ct));
        outbox.MapGet("/messages", (
            OutboxStatus? status,
            int? take,
            IOutboxAdminService service,
            CancellationToken ct) =>
            service.GetMessagesAsync(status, take ?? 200, ct));
        outbox.MapPost("/{messageId:guid}/retry", async (
            Guid messageId,
            IOutboxAdminService service,
            CancellationToken ct) =>
        {
            await service.RetryAsync(messageId, ct);
            return Results.NoContent();
        });
        return api;
    }
}
