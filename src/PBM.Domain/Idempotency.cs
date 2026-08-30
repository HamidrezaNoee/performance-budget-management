namespace PBM.Domain;

public enum IdempotencyRecordStatus
{
    Processing,
    Completed,
    Uncertain
}

public sealed class IdempotencyRecord : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string Key { get; set; }
    public required string Scope { get; set; }
    public required string RequestHash { get; set; }
    public IdempotencyRecordStatus Status { get; set; } = IdempotencyRecordStatus.Processing;
    public string? CorrelationId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureType { get; set; }
    public AppUser? User { get; set; }
}
