namespace PBM.Domain;

public sealed class CurrencyDefinition : Entity
{
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Symbol { get; set; }
    public bool IsBaseCurrency { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
}

public sealed class FxRateSource : Entity
{
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
}

public sealed class FxRate : Entity
{
    public Guid SourceId { get; set; }
    public Guid FromCurrencyId { get; set; }
    public Guid ToCurrencyId { get; set; }
    public DateTime RateDate { get; set; }
    public decimal Rate { get; set; }
    public string? Note { get; set; }
    public FxRateSource? Source { get; set; }
    public CurrencyDefinition? FromCurrency { get; set; }
    public CurrencyDefinition? ToCurrency { get; set; }
}

public sealed class StrategicObjective : Entity
{
    public Guid TenantId { get; set; }
    public Guid? ParentId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal Weight { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public StrategicObjective? Parent { get; set; }
    public ICollection<StrategicObjective> Children { get; set; } = [];
}

public sealed class KpiObjectiveLink
{
    public Guid KpiId { get; set; }
    public Guid ObjectiveId { get; set; }
    public decimal Weight { get; set; } = 1m;
    public KpiDefinition? Kpi { get; set; }
    public StrategicObjective? Objective { get; set; }
}
