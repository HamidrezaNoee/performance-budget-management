namespace PBM.Application;

public sealed record BudgetScenarioDto(Guid Id, string Code, string Name, bool IsActive);
public sealed record CreateBudgetScenarioRequest(string Code, string Name);
public sealed record UpdateBudgetScenarioRequest(string Name, bool IsActive);

public interface IScenarioService
{
    Task<IReadOnlyList<BudgetScenarioDto>> GetAsync(CancellationToken cancellationToken = default);
    Task<BudgetScenarioDto> CreateAsync(CreateBudgetScenarioRequest request, CancellationToken cancellationToken = default);
    Task<BudgetScenarioDto> UpdateAsync(Guid scenarioId, UpdateBudgetScenarioRequest request, CancellationToken cancellationToken = default);
}
