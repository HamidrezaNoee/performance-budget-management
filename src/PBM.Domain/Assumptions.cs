namespace PBM.Domain;

public sealed class AssumptionDefinition : Entity
{
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Unit { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public ICollection<AssumptionValue> Values { get; set; } = [];
}

/// <summary>
/// Stores a budgeting assumption at company/fiscal-year scope.
/// ScenarioId = null means the value applies to all scenarios.
/// PeriodId = null means annual/default value; a period-specific value overrides it.
/// ScopeKey is persisted so SQL Server can enforce uniqueness even when nullable scope columns are used.
/// </summary>
public sealed class AssumptionValue : Entity
{
    public Guid DefinitionId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public Guid? ScenarioId { get; set; }
    public Guid? PeriodId { get; set; }
    public required string ScopeKey { get; set; }
    public decimal Value { get; set; }
    public string? Source { get; set; }
    public string? Note { get; set; }

    public AssumptionDefinition? Definition { get; set; }
    public Company? Company { get; set; }
    public FiscalYear? FiscalYear { get; set; }
    public BudgetScenario? Scenario { get; set; }
    public FiscalPeriod? Period { get; set; }
}

public static class AssumptionScopeKey
{
    public static string Create(Guid? scenarioId, Guid? periodId) =>
        $"S:{(scenarioId.HasValue ? scenarioId.Value.ToString("N") : "GLOBAL")}|P:{(periodId.HasValue ? periodId.Value.ToString("N") : "ANNUAL")}";
}
