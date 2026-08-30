namespace PBM.Application;

public enum IdempotencyBeginDisposition
{
    Acquired,
    AlreadyCompleted,
    AlreadyProcessing,
    Uncertain,
    PayloadConflict
}

public sealed record IdempotencyBeginResult(
    IdempotencyBeginDisposition Disposition,
    Guid? RecordId,
    string? OriginalCorrelationId,
    DateTime? ExpiresAtUtc);

public interface IIdempotencyService
{
    Task<IdempotencyBeginResult> BeginAsync(
        string key,
        string scope,
        string requestHash,
        string correlationId,
        TimeSpan retention,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid recordId, CancellationToken cancellationToken = default);
    Task MarkUncertainAsync(Guid recordId, Exception exception, CancellationToken cancellationToken = default);
}
