using System.ComponentModel.DataAnnotations;

namespace PBM.Domain;

public sealed class IntegrationCredential : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(160)]
    public required string Name { get; set; }

    [MaxLength(80)]
    public required string ClientId { get; set; }

    [MaxLength(64)]
    public required string SecretHash { get; set; }

    [MaxLength(32)]
    public required string SecretSalt { get; set; }

    public int SecretIterations { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }

    [MaxLength(500)]
    public string? RevocationReason { get; set; }

    public bool IsActive(DateTime utcNow) =>
        RevokedAtUtc is null && (!ExpiresAtUtc.HasValue || ExpiresAtUtc.Value > utcNow);

    public AppUser? User { get; set; }
}
