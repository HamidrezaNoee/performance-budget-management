using System.ComponentModel.DataAnnotations;

namespace PBM.Domain;

public enum OutboxStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    DeadLetter = 3
}

public sealed class OutboxMessage : Entity
{
    public Guid TenantId { get; set; }

    [MaxLength(100)]
    public required string MessageType { get; set; }

    [MaxLength(100)]
    public required string Destination { get; set; }

    public required string PayloadJson { get; set; }

    [MaxLength(128)]
    public string? CorrelationId { get; set; }

    [MaxLength(200)]
    public string? DeduplicationKey { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntilUtc { get; set; }
    public Guid? LockToken { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public Tenant? Tenant { get; set; }
}
