using PBM.Application;

namespace PBM.Api;

public static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/notifications");

        notifications.MapGet("/", (
            bool? unreadOnly,
            int? take,
            INotificationService service,
            CancellationToken ct) =>
            service.GetMineAsync(unreadOnly ?? false, take ?? 50, ct));

        notifications.MapGet("/unread-count", async (
            INotificationService service,
            CancellationToken ct) =>
            Results.Ok(new { count = await service.GetUnreadCountAsync(ct) }));

        notifications.MapPost("/{notificationId:guid}/read", async (
            Guid notificationId,
            INotificationService service,
            CancellationToken ct) =>
        {
            await service.MarkReadAsync(notificationId, ct);
            return Results.NoContent();
        });

        notifications.MapPost("/read-all", async (
            INotificationService service,
            CancellationToken ct) =>
        {
            await service.MarkAllReadAsync(ct);
            return Results.NoContent();
        });

        return api;
    }
}
