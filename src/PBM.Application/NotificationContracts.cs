using PBM.Domain;

namespace PBM.Application;

public sealed record NotificationDto(
    Guid Id,
    Guid? CompanyId,
    string Category,
    string Title,
    string Message,
    NotificationSeverity Severity,
    string? EntityType,
    string? EntityId,
    string? ActionUrl,
    bool IsRead,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    DateTime? ExpiresAtUtc);

public sealed record NotificationDispatchRequest(
    IReadOnlyCollection<Guid> UserIds,
    Guid? CompanyId,
    string Category,
    string Title,
    string Message,
    NotificationSeverity Severity = NotificationSeverity.Info,
    string? EntityType = null,
    string? EntityId = null,
    string? ActionUrl = null,
    DateTime? ExpiresAtUtc = null);

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> GetMineAsync(bool unreadOnly = false, int take = 50, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);
    Task MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task MarkAllReadAsync(CancellationToken cancellationToken = default);
    Task DispatchAsync(NotificationDispatchRequest request, CancellationToken cancellationToken = default);
}
