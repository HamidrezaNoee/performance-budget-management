namespace PBM.Domain;

public enum NotificationSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public sealed class Notification : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? CompanyId { get; set; }
    public required string Category { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    public AppUser? User { get; set; }
    public Company? Company { get; set; }
}
