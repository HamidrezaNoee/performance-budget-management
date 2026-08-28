namespace PBM.Domain;

public enum CapexProjectStatus
{
    Proposed,
    Submitted,
    Approved,
    InProgress,
    OnHold,
    Completed,
    Cancelled
}

public enum CapexPriority
{
    Low,
    Normal,
    High,
    Critical
}

public sealed class CapexProject : Entity
{
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ProjectDimensionMemberId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public CapexProjectStatus Status { get; set; } = CapexProjectStatus.Proposed;
    public CapexPriority Priority { get; set; } = CapexPriority.Normal;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal? ApprovedBudgetLimit { get; set; }
    public string CurrencyCode { get; set; } = "IRR";
    public Guid? OwnerOrganizationUnitId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public decimal CompletionPercent { get; set; }
    public string? LastDecisionComment { get; set; }
    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public Company? Company { get; set; }
    public DimensionMember? ProjectDimensionMember { get; set; }
    public OrganizationUnit? OwnerOrganizationUnit { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public ICollection<CapexMilestone> Milestones { get; set; } = [];
}

public sealed class CapexMilestone : Entity
{
    public Guid ProjectId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Weight { get; set; }
    public decimal ProgressPercent { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Note { get; set; }
    public CapexProject? Project { get; set; }
}
