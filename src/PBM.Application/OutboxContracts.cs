using PBM.Domain;

namespace PBM.Application;

public sealed record OutboxMessageDto(
    Guid Id,
    string MessageType,
    string Destination,
    OutboxStatus Status,
    int Attempts,
    DateTime NextAttemptAtUtc,
    DateTime? LockedUntilUtc,
    DateTime? CompletedAtUtc,
    string? CorrelationId,
    string? DeduplicationKey,
    string? LastError,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record OutboxSummaryDto(
    int Pending,
    int Processing,
    int Completed,
    int DeadLetter);

public interface IOutboxAdminService
{
    Task<OutboxSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboxMessageDto>> GetMessagesAsync(
        OutboxStatus? status = null,
        int take = 200,
        CancellationToken cancellationToken = default);
    Task RetryAsync(Guid messageId, CancellationToken cancellationToken = default);
}
