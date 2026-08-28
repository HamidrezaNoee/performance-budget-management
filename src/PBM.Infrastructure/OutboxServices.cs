using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed record OutboxDispatchItem(
    Guid Id,
    Guid TenantId,
    string MessageType,
    string Destination,
    string PayloadJson,
    string? CorrelationId,
    int Attempts,
    Guid LockToken);

public sealed record NotificationWebhookEnvelope(
    Guid EventId,
    Guid TenantId,
    DateTime OccurredAtUtc,
    IReadOnlyCollection<Guid> UserIds,
    Guid? CompanyId,
    string Category,
    string Title,
    string Message,
    NotificationSeverity Severity,
    string? EntityType,
    string? EntityId,
    string? ActionUrl,
    DateTime? ExpiresAtUtc,
    string? CorrelationId);

public sealed class OutboxWriter(
    PbmDbContext db,
    IUserContext currentUser,
    IConfiguration configuration)
{
    public bool IsNotificationWebhookEnabled =>
        configuration.GetValue<bool>("OutboundNotifications:Webhook:Enabled")
        && !string.IsNullOrWhiteSpace(configuration["OutboundNotifications:Webhook:Url"]);

    public OutboxMessage? EnqueueNotificationWebhook(NotificationDispatchRequest request)
    {
        if (!IsNotificationWebhookEnabled) return null;
        var message = new OutboxMessage
        {
            TenantId = currentUser.TenantId,
            MessageType = "notification.webhook.v1",
            Destination = "notification-webhook",
            PayloadJson = "{}",
            CorrelationId = Activity.Current?.TraceId.ToString(),
            Status = OutboxStatus.Pending,
            NextAttemptAtUtc = DateTime.UtcNow
        };
        var envelope = new NotificationWebhookEnvelope(
            message.Id,
            message.TenantId,
            message.CreatedAtUtc,
            request.UserIds,
            request.CompanyId,
            request.Category,
            request.Title,
            request.Message,
            request.Severity,
            request.EntityType,
            request.EntityId,
            request.ActionUrl,
            request.ExpiresAtUtc,
            message.CorrelationId);
        message.PayloadJson = JsonSerializer.Serialize(envelope);
        db.Set<OutboxMessage>().Add(message);
        return message;
    }
}

public sealed class OutboxQueueService(
    PbmDbContext db,
    SqlApplicationLock applicationLock,
    IConfiguration configuration)
{
    public async Task<IReadOnlyList<OutboxDispatchItem>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(configuration.GetValue<int?>("Outbox:BatchSize") ?? 20, 1, 200);
        var lockSeconds = Math.Clamp(configuration.GetValue<int?>("Outbox:LockSeconds") ?? 120, 30, 900);
        var now = DateTime.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await applicationLock.AcquireAsync("pbm-outbox-claim", cancellationToken);
        var candidates = await db.Set<OutboxMessage>()
            .Where(x =>
                ((x.Status == OutboxStatus.Pending && x.NextAttemptAtUtc <= now)
                 || (x.Status == OutboxStatus.Processing && x.LockedUntilUtc < now))
                && x.CompletedAtUtc == null)
            .OrderBy(x => x.NextAttemptAtUtc)
            .ThenBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var result = new List<OutboxDispatchItem>(candidates.Count);
        foreach (var item in candidates)
        {
            var token = Guid.NewGuid();
            item.Status = OutboxStatus.Processing;
            item.Attempts++;
            item.LockToken = token;
            item.LockedUntilUtc = now.AddSeconds(lockSeconds);
            item.UpdatedAtUtc = now;
            result.Add(new OutboxDispatchItem(
                item.Id,
                item.TenantId,
                item.MessageType,
                item.Destination,
                item.PayloadJson,
                item.CorrelationId,
                item.Attempts,
                token));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task CompleteAsync(OutboxDispatchItem item, CancellationToken cancellationToken)
    {
        var entity = await db.Set<OutboxMessage>().SingleOrDefaultAsync(x =>
            x.Id == item.Id
            && x.Status == OutboxStatus.Processing
            && x.LockToken == item.LockToken,
            cancellationToken);
        if (entity is null) return;

        var now = DateTime.UtcNow;
        entity.Status = OutboxStatus.Completed;
        entity.CompletedAtUtc = now;
        entity.LockedUntilUtc = null;
        entity.LockToken = null;
        entity.LastError = null;
        entity.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(OutboxDispatchItem item, Exception exception, CancellationToken cancellationToken)
    {
        var entity = await db.Set<OutboxMessage>().SingleOrDefaultAsync(x =>
            x.Id == item.Id
            && x.Status == OutboxStatus.Processing
            && x.LockToken == item.LockToken,
            cancellationToken);
        if (entity is null) return;

        var maxAttempts = Math.Clamp(configuration.GetValue<int?>("Outbox:MaxAttempts") ?? 8, 1, 50);
        var baseDelaySeconds = Math.Clamp(configuration.GetValue<int?>("Outbox:BaseDelaySeconds") ?? 15, 1, 3600);
        var maxDelaySeconds = Math.Clamp(configuration.GetValue<int?>("Outbox:MaxDelaySeconds") ?? 3600, 30, 86400);
        var now = DateTime.UtcNow;
        entity.LastError = Truncate($"{exception.GetType().Name}: {exception.Message}", 2000);
        entity.LockedUntilUtc = null;
        entity.LockToken = null;
        entity.UpdatedAtUtc = now;

        if (entity.Attempts >= maxAttempts)
        {
            entity.Status = OutboxStatus.DeadLetter;
            entity.NextAttemptAtUtc = now;
        }
        else
        {
            var multiplier = Math.Pow(2, Math.Min(entity.Attempts - 1, 16));
            var delaySeconds = Math.Min(maxDelaySeconds, baseDelaySeconds * multiplier);
            entity.Status = OutboxStatus.Pending;
            entity.NextAttemptAtUtc = now.AddSeconds(delaySeconds);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

public sealed class OutboxAdminService(
    PbmDbContext db,
    IUserContext user) : IOutboxAdminService
{
    public async Task<OutboxSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanView();
        var counts = await db.Set<OutboxMessage>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId)
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);
        return new OutboxSummaryDto(
            counts.GetValueOrDefault(OutboxStatus.Pending),
            counts.GetValueOrDefault(OutboxStatus.Processing),
            counts.GetValueOrDefault(OutboxStatus.Completed),
            counts.GetValueOrDefault(OutboxStatus.DeadLetter));
    }

    public async Task<IReadOnlyList<OutboxMessageDto>> GetMessagesAsync(
        OutboxStatus? status = null,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        EnsureCanView();
        take = Math.Clamp(take, 1, 1000);
        var query = db.Set<OutboxMessage>().AsNoTracking().Where(x => x.TenantId == user.TenantId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return await query
            .OrderByDescending(x => x.Status == OutboxStatus.DeadLetter)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .Take(take)
            .Select(x => new OutboxMessageDto(
                x.Id,
                x.MessageType,
                x.Destination,
                x.Status,
                x.Attempts,
                x.NextAttemptAtUtc,
                x.LockedUntilUtc,
                x.CompletedAtUtc,
                x.CorrelationId,
                x.DeduplicationKey,
                x.LastError,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task RetryAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        EnsureCanRetry();
        var message = await db.Set<OutboxMessage>().SingleOrDefaultAsync(x =>
            x.Id == messageId && x.TenantId == user.TenantId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Outbox message was not found.");
        if (message.Status != OutboxStatus.DeadLetter)
            throw new InvalidOperationException("Only dead-letter messages can be manually retried.");

        message.Status = OutboxStatus.Pending;
        message.Attempts = 0;
        message.NextAttemptAtUtc = DateTime.UtcNow;
        message.LockedUntilUtc = null;
        message.LockToken = null;
        message.CompletedAtUtc = null;
        message.LastError = null;
        message.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "OutboxMessage",
            EntityId = message.Id.ToString(),
            Action = "OUTBOX_RETRY",
            NewValueJson = JsonSerializer.Serialize(new { message.MessageType, message.Destination })
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private void EnsureCanView()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("AUDITOR") || user.IsInRole("CFO") || user.IsInRole("BUDGET_MANAGER")) return;
        throw new UnauthorizedAccessException("Administrator, auditor, CFO or budget manager role is required to view the outbox.");
    }

    private void EnsureCanRetry()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN")) return;
        throw new UnauthorizedAccessException("Administrator role is required to retry dead-letter messages.");
    }
}
