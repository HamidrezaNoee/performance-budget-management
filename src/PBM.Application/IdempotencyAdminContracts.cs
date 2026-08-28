using PBM.Domain;

namespace PBM.Application;

public enum IdempotencyResolutionAction
{
    MarkCompleted,
    ReleaseForRetry
}

public sealed record IdempotencyAdminDto(
    Guid Id,
    Guid UserId,
    string UserDisplayName,
    string Key,
    string Scope,
    IdempotencyRecordStatus Status,
    string? CorrelationId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? CompletedAtUtc,
    string? FailureType);

public sealed record ResolveIdempotencyRequest(
    IdempotencyResolutionAction Action,
    string Comment);

public interface IIdempotencyAdminService
{
    Task<IReadOnlyList<IdempotencyAdminDto>> GetAsync(
        IdempotencyRecordStatus? status = null,
        int take = 200,
        CancellationToken cancellationToken = default);

    Task ResolveAsync(
        Guid recordId,
        ResolveIdempotencyRequest request,
        CancellationToken cancellationToken = default);

    Task<int> CleanupExpiredCompletedAsync(
        int take = 1000,
        CancellationToken cancellationToken = default);
}
