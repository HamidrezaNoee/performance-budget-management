namespace PBM.Domain;

public enum BudgetStatus { Draft, Submitted, UnderReview, Returned, Approved, Rejected, Revised, Closed }
public enum ValueKind { Budget, Actual, Commitment, Forecast }
public enum MeasureAggregation { Sum, Average, Min, Max, LastNonEmpty, None }
public enum MeasureValueType { Amount, Quantity, Rate, Percentage, Score }

public sealed class DimensionDefinition : Entity
{
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsSystem { get; set; }
    public bool IsHierarchical { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public ICollection<DimensionMember> Members { get; set; } = [];
}

public sealed class DimensionMember : Entity
{
    public Guid DimensionId { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? ExternalKey { get; set; }
    public string? MetadataJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DimensionDefinition? Dimension { get; set; }
    public DimensionMember? Parent { get; set; }
    public Company? Company { get; set; }
    public ICollection<DimensionMember> Children { get; set; } = [];
}

public sealed class BudgetModel : Entity
{
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public ICollection<BudgetModelDimension> Dimensions { get; set; } = [];
    public ICollection<MeasureDefinition> Measures { get; set; } = [];
}

public sealed class BudgetModelDimension
{
    public Guid BudgetModelId { get; set; }
    public Guid DimensionId { get; set; }
    public int Sequence { get; set; }
    public bool IsRequired { get; set; } = true;
    public BudgetModel? BudgetModel { get; set; }
    public DimensionDefinition? Dimension { get; set; }
}

public sealed class MeasureDefinition : Entity
{
    public Guid BudgetModelId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Unit { get; set; }
    public MeasureValueType ValueType { get; set; } = MeasureValueType.Amount;
    public MeasureAggregation Aggregation { get; set; } = MeasureAggregation.Sum;
    public bool IsCalculated { get; set; }
    public string? FormulaExpression { get; set; }
    public int DisplayOrder { get; set; }
    public BudgetModel? BudgetModel { get; set; }
}

public sealed class BudgetScenario : Entity
{
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
}

public sealed class BudgetPlan : Entity
{
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public Guid BudgetModelId { get; set; }
    public required string Name { get; set; }
    public BudgetStatus Status { get; set; } = BudgetStatus.Draft;
    public Company? Company { get; set; }
    public FiscalYear? FiscalYear { get; set; }
    public BudgetModel? BudgetModel { get; set; }
    public ICollection<BudgetVersion> Versions { get; set; } = [];
}

public sealed class BudgetVersion : Entity
{
    public Guid BudgetPlanId { get; set; }
    public Guid ScenarioId { get; set; }
    public int VersionNumber { get; set; } = 1;
    public required string Name { get; set; }
    public BudgetStatus Status { get; set; } = BudgetStatus.Draft;
    public bool IsLocked { get; set; }
    public BudgetPlan? BudgetPlan { get; set; }
    public BudgetScenario? Scenario { get; set; }
    public ICollection<BudgetFact> Facts { get; set; } = [];
}

public sealed class BudgetFact : Entity
{
    public Guid VersionId { get; set; }
    public Guid PeriodId { get; set; }
    public Guid MeasureId { get; set; }
    public ValueKind ValueKind { get; set; }
    public decimal Value { get; set; }
    public string? CurrencyCode { get; set; }
    public required string CoordinateHash { get; set; }
    public required string CoordinatesJson { get; set; }
    public string? Source { get; set; }
    public string? Note { get; set; }
    public BudgetVersion? Version { get; set; }
    public FiscalPeriod? Period { get; set; }
    public MeasureDefinition? Measure { get; set; }
    public ICollection<BudgetFactDimension> Dimensions { get; set; } = [];
}

public sealed class BudgetFactDimension
{
    public Guid BudgetFactId { get; set; }
    public Guid DimensionId { get; set; }
    public Guid MemberId { get; set; }
    public BudgetFact? BudgetFact { get; set; }
    public DimensionDefinition? Dimension { get; set; }
    public DimensionMember? Member { get; set; }
}

public sealed class KpiDefinition : Entity
{
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public decimal Weight { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public string Frequency { get; set; } = "Monthly";
    public string? FormulaExpression { get; set; }
    public Tenant? Tenant { get; set; }
}

public sealed class KpiValue : Entity
{
    public Guid KpiId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PeriodId { get; set; }
    public decimal Target { get; set; }
    public decimal Actual { get; set; }
    public decimal? Score { get; set; }
    public KpiDefinition? Kpi { get; set; }
    public Company? Company { get; set; }
    public FiscalPeriod? Period { get; set; }
}

public sealed class BudgetComment : Entity
{
    public Guid VersionId { get; set; }
    public Guid UserId { get; set; }
    public required string Text { get; set; }
    public BudgetVersion? Version { get; set; }
    public AppUser? User { get; set; }
}

public sealed class AuditLog : Entity
{
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Action { get; set; }
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string? IpAddress { get; set; }
}
