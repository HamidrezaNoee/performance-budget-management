namespace PBM.Domain;

public sealed class LicenseSubscription : Entity
{
    public Guid TenantId { get; set; }
    public required string LicenseKey { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int MaxCompanies { get; set; } = 1;
    public int MaxUsers { get; set; } = 5;
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
}

public sealed class AppUser : Entity
{
    public Guid TenantId { get; set; }
    public required string UserName { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public int TokenVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = [];
    public ICollection<UserCompanyAccess> CompanyAccess { get; set; } = [];
    public ICollection<IdempotencyRecord> IdempotencyRecords { get; set; } = [];
    public ICollection<ActualLedgerEntry> ActualLedgerEntries { get; set; } = [];
}

public sealed class Role : Entity
{
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = [];
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public AppUser? User { get; set; }
    public Role? Role { get; set; }
}

public sealed class UserCompanyAccess
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public bool CanRead { get; set; } = true;
    public bool CanWrite { get; set; }
    public AppUser? User { get; set; }
    public Company? Company { get; set; }
}
