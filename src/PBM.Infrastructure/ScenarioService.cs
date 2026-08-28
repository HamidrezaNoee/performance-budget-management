using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ScenarioService(PbmDbContext db, IUserContext user) : IScenarioService
{
    public async Task<IReadOnlyList<BudgetScenarioDto>> GetAsync(CancellationToken cancellationToken = default) =>
        await db.BudgetScenarios.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.Code)
            .Select(x => new BudgetScenarioDto(x.Id, x.Code, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<BudgetScenarioDto> CreateAsync(CreateBudgetScenarioRequest request, CancellationToken cancellationToken = default)
    {
        EnsureManager();
        var code = NormalizeCode(request.Code);
        var name = NormalizeName(request.Name);
        if (await db.BudgetScenarios.AnyAsync(x => x.TenantId == user.TenantId && x.Code == code, cancellationToken))
            throw new InvalidOperationException("A budget scenario with this code already exists.");

        var scenario = new BudgetScenario { TenantId = user.TenantId, Code = code, Name = name, IsActive = true };
        db.BudgetScenarios.Add(scenario);
        AddAudit(scenario.Id, "CREATE", null, new { scenario.Code, scenario.Name, scenario.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return Map(scenario);
    }

    public async Task<BudgetScenarioDto> UpdateAsync(Guid scenarioId, UpdateBudgetScenarioRequest request, CancellationToken cancellationToken = default)
    {
        EnsureManager();
        var scenario = await db.BudgetScenarios.SingleOrDefaultAsync(x => x.Id == scenarioId && x.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget scenario was not found.");
        if (scenario.Code == "BASE" && !request.IsActive)
            throw new InvalidOperationException("The BASE scenario cannot be disabled.");

        var old = new { scenario.Name, scenario.IsActive };
        scenario.Name = NormalizeName(request.Name);
        scenario.IsActive = request.IsActive;
        scenario.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(scenario.Id, "UPDATE", old, new { scenario.Name, scenario.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return Map(scenario);
    }

    private void EnsureManager()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("BUDGET_MANAGER") || user.IsInRole("CFO")) return;
        throw new UnauthorizedAccessException("Budget manager, CFO or administrator role is required to manage scenarios.");
    }

    private static string NormalizeCode(string? value)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 64 || code.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')))
            throw new ArgumentException("Scenario code must contain 2-64 letters, numbers, underscore, dash or dot characters.");
        return code;
    }

    private static string NormalizeName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 200) throw new ArgumentException("Scenario name is required and must be at most 200 characters.");
        return name;
    }

    private void AddAudit(Guid entityId, string action, object? oldValue, object? newValue) => db.AuditLogs.Add(new AuditLog
    {
        TenantId = user.TenantId,
        UserId = user.UserId == Guid.Empty ? null : user.UserId,
        EntityType = "BudgetScenario",
        EntityId = entityId.ToString(),
        Action = action,
        OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
        NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue)
    });

    private static BudgetScenarioDto Map(BudgetScenario scenario) => new(scenario.Id, scenario.Code, scenario.Name, scenario.IsActive);
}
