namespace PBM.Domain;

public sealed class IntegrationCredential : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required string ClientId { get; set; }
    public required string SecretHash { get; set; }
    public required string SecretSalt { get; set; }
    public int SecretIterations { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }

    public bool IsActive(DateTime utcNow) =>
        RevokedAtUtc is null && (!ExpiresAtUtc.HasValue || ExpiresAtUtc.Value > utcNow);

    public AppUser? User { get; set; }
}
