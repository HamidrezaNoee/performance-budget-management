using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class IdempotencyAdminService(
    PbmDbContext db,
    IUserContext user,
    IConfiguration configuration) : IIdempotencyAdminService
{
    public async Task<IReadOnlyList<IdempotencyAdminDto>> GetAsync(
        IdempotencyRecordStatus? status = null,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        EnsureViewerRole();
        take = Math.Clamp(take, 1, 1000);
        var query = db.Set<IdempotencyRecord>().AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.TenantId == user.TenantId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        return await query.OrderByDescending(x => x.UpdatedAtUtc)
            .Take(take)
            .Select(x => new IdempotencyAdminDto(
                x.Id,
                x.UserId,
                x.User!.DisplayName,
                x.Key,
                x.Scope,
                x.Status,
                x.CorrelationId,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.ExpiresAtUtc,
                x.CompletedAtUtc,
                x.FailureType))
            .ToListAsync(cancellationToken);
    }

    public async Task ResolveAsync(
        Guid recordId,
        ResolveIdempotencyRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAdminRole();
        var comment = (request.Comment ?? string.Empty).Trim();
        if (comment.Length is < 5 or > 2000)
            throw new ArgumentException("A reconciliation comment between 5 and 2000 characters is required.");

        var record = await db.Set<IdempotencyRecord>()
            .SingleOrDefaultAsync(x => x.Id == recordId && x.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Idempotency record was not found.");
        if (record.Status != IdempotencyRecordStatus.Uncertain)
            throw new InvalidOperationException("Only an Uncertain idempotency record can be reconciled manually.");

        var snapshot = new
        {
            record.Key,
            record.Scope,
            record.Status,
            record.CorrelationId,
            record.FailureType,
            record.CreatedAtUtc,
            record.UpdatedAtUtc
        };

        if (request.Action == IdempotencyResolutionAction.MarkCompleted)
        {
            var retentionHours = configuration.GetValue<int?>("Idempotency:RetentionHours") ?? 168;
            retentionHours = Math.Clamp(retentionHours, 1, 720);
            record.Status = IdempotencyRecordStatus.Completed;
            record.CompletedAtUtc = DateTime.UtcNow;
            record.ExpiresAtUtc = DateTime.UtcNow.AddHours(retentionHours);
            record.FailureType = "ManuallyReconciledAsCompleted";
            record.UpdatedAtUtc = DateTime.UtcNow;
            AddAudit(record.Id, "IDEMPOTENCY_MARK_COMPLETED", snapshot, new { request.Action, Comment = comment, record.ExpiresAtUtc });
        }
        else if (request.Action == IdempotencyResolutionAction.ReleaseForRetry)
        {
            AddAudit(record.Id, "IDEMPOTENCY_RELEASE_FOR_RETRY", snapshot, new { request.Action, Comment = comment, Released = true });
            db.Set<IdempotencyRecord>().Remove(record);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(request.Action), "Unsupported idempotency reconciliation action.");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CleanupExpiredCompletedAsync(int take = 1000, CancellationToken cancellationToken = default)
    {
        EnsureAdminRole();
        take = Math.Clamp(take, 1, 5000);
        var now = DateTime.UtcNow;
        var records = await db.Set<IdempotencyRecord>()
            .Where(x => x.TenantId == user.TenantId
                && x.Status == IdempotencyRecordStatus.Completed
                && x.ExpiresAtUtc <= now)
            .OrderBy(x => x.ExpiresAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (records.Count == 0) return 0;

        db.Set<IdempotencyRecord>().RemoveRange(records);
        AddAudit(Guid.NewGuid(), "IDEMPOTENCY_CLEANUP", null, new { Removed = records.Count, Utc = now });
        await db.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    private void EnsureViewerRole()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("AUDITOR") || user.IsInRole("CFO")) return;
        throw new UnauthorizedAccessException("Administrator, auditor or CFO role is required to view idempotency records.");
    }

    private void EnsureAdminRole()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN")) return;
        throw new UnauthorizedAccessException("Administrator role is required to reconcile idempotency records.");
    }

    private void AddAudit(Guid entityId, string action, object? oldValue, object? newValue) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "IdempotencyRecord",
            EntityId = entityId.ToString(),
            Action = action,
            OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue)
        });
}
