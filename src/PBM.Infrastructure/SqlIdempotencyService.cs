using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class SqlIdempotencyService(PbmDbContext db, IUserContext user) : IIdempotencyService
{
    public async Task<IdempotencyBeginResult> BeginAsync(
        string key,
        string scope,
        string requestHash,
        string correlationId,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        if (user.UserId == Guid.Empty || user.TenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Authenticated user is required for idempotent writes.");
        if (retention <= TimeSpan.Zero || retention > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(retention), "Idempotency retention must be greater than zero and at most 30 days.");

        var normalizedKey = NormalizeRequired(key, 100, "Idempotency key");
        var normalizedScope = NormalizeRequired(scope, 240, "Idempotency scope");
        var normalizedHash = NormalizeHash(requestHash);
        var now = DateTime.UtcNow;
        var resource = BuildLockResource(user.TenantId, user.UserId, normalizedScope, normalizedKey);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var lockResult = new SqlParameter("@lockResult", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var resourceParameter = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @lockResult = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000;",
            [lockResult, resourceParameter],
            cancellationToken);
        var acquired = lockResult.Value is int value && value >= 0;
        if (!acquired)
            throw new TimeoutException("PBM could not acquire the idempotency lock within 5 seconds.");

        var records = db.Set<IdempotencyRecord>();
        var existing = await records.SingleOrDefaultAsync(x =>
            x.TenantId == user.TenantId
            && x.UserId == user.UserId
            && x.Scope == normalizedScope
            && x.Key == normalizedKey,
            cancellationToken);

        if (existing is not null && existing.ExpiresAtUtc <= now)
        {
            if (existing.Status == IdempotencyRecordStatus.Completed)
            {
                records.Remove(existing);
                await db.SaveChangesAsync(cancellationToken);
                existing = null;
            }
            else if (existing.Status == IdempotencyRecordStatus.Processing)
            {
                existing.Status = IdempotencyRecordStatus.Uncertain;
                existing.FailureType = "StaleProcessingTimeout";
                existing.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            // Uncertain records intentionally never auto-expire. They require explicit business reconciliation.
        }

        if (existing is not null)
        {
            var disposition = !string.Equals(existing.RequestHash, normalizedHash, StringComparison.Ordinal)
                ? IdempotencyBeginDisposition.PayloadConflict
                : existing.Status switch
                {
                    IdempotencyRecordStatus.Completed => IdempotencyBeginDisposition.AlreadyCompleted,
                    IdempotencyRecordStatus.Processing => IdempotencyBeginDisposition.AlreadyProcessing,
                    IdempotencyRecordStatus.Uncertain => IdempotencyBeginDisposition.Uncertain,
                    _ => IdempotencyBeginDisposition.Uncertain
                };
            await transaction.CommitAsync(cancellationToken);
            return new IdempotencyBeginResult(disposition, existing.Id, existing.CorrelationId, existing.ExpiresAtUtc);
        }

        var record = new IdempotencyRecord
        {
            TenantId = user.TenantId,
            UserId = user.UserId,
            Key = normalizedKey,
            Scope = normalizedScope,
            RequestHash = normalizedHash,
            Status = IdempotencyRecordStatus.Processing,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            ExpiresAtUtc = now.Add(retention)
        };
        records.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new IdempotencyBeginResult(IdempotencyBeginDisposition.Acquired, record.Id, record.CorrelationId, record.ExpiresAtUtc);
    }

    public async Task CompleteAsync(Guid recordId, CancellationToken cancellationToken = default)
    {
        var record = await FindOwnedRecordAsync(recordId, cancellationToken);
        if (record.Status == IdempotencyRecordStatus.Completed) return;
        if (record.Status != IdempotencyRecordStatus.Processing)
            throw new InvalidOperationException("Only a processing idempotency record can be completed.");

        record.Status = IdempotencyRecordStatus.Completed;
        record.CompletedAtUtc = DateTime.UtcNow;
        record.FailureType = null;
        record.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkUncertainAsync(Guid recordId, Exception exception, CancellationToken cancellationToken = default)
    {
        var record = await FindOwnedRecordAsync(recordId, cancellationToken);
        if (record.Status == IdempotencyRecordStatus.Completed) return;

        record.Status = IdempotencyRecordStatus.Uncertain;
        record.FailureType = exception.GetType().Name.Length <= 120
            ? exception.GetType().Name
            : exception.GetType().Name[..120];
        record.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IdempotencyRecord> FindOwnedRecordAsync(Guid recordId, CancellationToken cancellationToken) =>
        await db.Set<IdempotencyRecord>().SingleOrDefaultAsync(x =>
            x.Id == recordId && x.TenantId == user.TenantId && x.UserId == user.UserId,
            cancellationToken)
        ?? throw new KeyNotFoundException("Idempotency record was not found.");

    private static string BuildLockResource(Guid tenantId, Guid userId, string scope, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}|{userId:N}|{scope}|{key}"));
        return $"PBM:IDEMP:{Convert.ToHexString(bytes)}";
    }

    private static string NormalizeRequired(string value, int maxLength, string field)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 || normalized.Length > maxLength)
            throw new ArgumentException($"{field} must contain 1-{maxLength} characters.");
        return normalized;
    }

    private static string NormalizeHash(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("Request hash must be a 64-character SHA-256 hex value.");
        return normalized;
    }
}
