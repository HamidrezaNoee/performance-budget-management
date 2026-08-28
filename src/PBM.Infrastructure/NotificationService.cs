using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class NotificationService(PbmDbContext db, IUserContext currentUser) : INotificationService
{
    public async Task<IReadOnlyList<NotificationDto>> GetMineAsync(
        bool unreadOnly = false,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        take = Math.Clamp(take, 1, 200);
        var now = DateTime.UtcNow;
        var query = db.Notifications.AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId
                && x.UserId == currentUser.UserId
                && (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > now));
        if (unreadOnly) query = query.Where(x => !x.IsRead);

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .Select(x => new NotificationDto(
                x.Id,
                x.CompanyId,
                x.Category,
                x.Title,
                x.Message,
                x.Severity,
                x.EntityType,
                x.EntityId,
                x.ActionUrl,
                x.IsRead,
                x.CreatedAtUtc,
                x.ReadAtUtc,
                x.ExpiresAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var now = DateTime.UtcNow;
        return await db.Notifications.AsNoTracking().CountAsync(x =>
            x.TenantId == currentUser.TenantId
            && x.UserId == currentUser.UserId
            && !x.IsRead
            && (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > now), cancellationToken);
    }

    public async Task MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var item = await db.Notifications.SingleOrDefaultAsync(x =>
            x.Id == notificationId
            && x.TenantId == currentUser.TenantId
            && x.UserId == currentUser.UserId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (item.IsRead) return;
        item.IsRead = true;
        item.ReadAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var now = DateTime.UtcNow;
        var items = await db.Notifications.Where(x =>
            x.TenantId == currentUser.TenantId
            && x.UserId == currentUser.UserId
            && !x.IsRead
            && (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > now))
            .ToListAsync(cancellationToken);
        if (items.Count == 0) return;

        foreach (var item in items)
        {
            item.IsRead = true;
            item.ReadAtUtc = now;
            item.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DispatchAsync(NotificationDispatchRequest request, CancellationToken cancellationToken = default)
    {
        var userIds = (request.UserIds ?? Array.Empty<Guid>()).Where(x => x != Guid.Empty).Distinct().ToArray();
        if (userIds.Length == 0) return;

        var category = NormalizeRequired(request.Category, nameof(request.Category), 80);
        var title = NormalizeRequired(request.Title, nameof(request.Title), 200);
        var message = NormalizeRequired(request.Message, nameof(request.Message), 1200);
        var entityType = NormalizeOptional(request.EntityType, 80);
        var entityId = NormalizeOptional(request.EntityId, 120);
        var actionUrl = NormalizeOptional(request.ActionUrl, 500);
        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc <= DateTime.UtcNow)
            throw new ArgumentException("Notification expiration must be in the future.");

        if (request.CompanyId.HasValue)
        {
            var validCompany = await db.Companies.AsNoTracking().AnyAsync(x =>
                x.Id == request.CompanyId.Value && x.TenantId == currentUser.TenantId, cancellationToken);
            if (!validCompany) throw new ArgumentException("Notification company is outside the current tenant.");
        }

        var validUserIds = await db.Users.AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId && x.IsActive && userIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (validUserIds.Count != userIds.Length)
            throw new ArgumentException("One or more notification recipients are invalid or inactive.");

        foreach (var userId in validUserIds)
        {
            db.Notifications.Add(new Notification
            {
                TenantId = currentUser.TenantId,
                UserId = userId,
                CompanyId = request.CompanyId,
                Category = category,
                Title = title,
                Message = message,
                Severity = request.Severity,
                EntityType = entityType,
                EntityId = entityId,
                ActionUrl = actionUrl,
                ExpiresAtUtc = request.ExpiresAtUtc
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private void EnsureAuthenticated()
    {
        if (currentUser.UserId == Guid.Empty || currentUser.TenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Authenticated user is required.");
    }

    private static string NormalizeRequired(string? value, string field, int maxLength)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > maxLength)
            throw new ArgumentException($"{field} is required and must be at most {maxLength} characters.");
        return text;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length > maxLength) throw new ArgumentException($"Value must be at most {maxLength} characters.");
        return text;
    }
}
