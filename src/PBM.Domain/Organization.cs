namespace PBM.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Tenant : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Company> Companies { get; set; } = [];
    public ICollection<OutboxMessage> OutboxMessages { get; set; } = [];
}

public sealed class Company : Entity
{
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Industry { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public ICollection<OrganizationUnit> OrganizationUnits { get; set; } = [];
    public ICollection<FiscalYear> FiscalYears { get; set; } = [];
}

public sealed class OrganizationUnit : Entity
{
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string UnitType { get; set; } = "Department";
    public bool IsActive { get; set; } = true;
    public Company? Company { get; set; }
    public OrganizationUnit? Parent { get; set; }
    public ICollection<OrganizationUnit> Children { get; set; } = [];
}

public sealed class FiscalYear : Entity
{
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int JalaliYear { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsClosed { get; set; }
    public Company? Company { get; set; }
    public ICollection<FiscalPeriod> Periods { get; set; } = [];
}

public sealed class FiscalPeriod : Entity
{
    public Guid FiscalYearId { get; set; }
    public int Sequence { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int JalaliMonth { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsClosed { get; set; }
    public FiscalYear? FiscalYear { get; set; }
}
