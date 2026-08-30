using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class StrategyService(PbmDbContext db, IUserContext user) : IStrategyService
{
    public async Task<IReadOnlyList<StrategicObjectiveDto>> GetObjectivesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.StrategicObjectives.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId);
        if (!includeInactive) query = query.Where(x => x.IsActive);

        return await query
            .OrderBy(x => x.Code)
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<StrategicObjectiveDto> CreateObjectiveAsync(
        CreateStrategicObjectiveRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureEditor();
        var code = NormalizeRequired(request.Code, 80, "Objective code").ToUpperInvariant();
        var name = NormalizeRequired(request.Name, 240, "Objective name");
        ValidateWeight(request.Weight, "Objective weight");

        if (await db.StrategicObjectives.AnyAsync(
                x => x.TenantId == user.TenantId && x.Code == code,
                cancellationToken))
            throw new InvalidOperationException("A strategic objective with this code already exists.");

        await ValidateParentAsync(null, request.ParentId, cancellationToken);
        var objective = new StrategicObjective
        {
            TenantId = user.TenantId,
            ParentId = request.ParentId,
            Code = code,
            Name = name,
            Description = NormalizeOptional(request.Description, 2000),
            Weight = request.Weight,
            IsActive = true
        };
        db.StrategicObjectives.Add(objective);
        AddAudit("StrategicObjective", objective.Id, "CREATE", null, new
        {
            objective.Code,
            objective.Name,
            objective.ParentId,
            objective.Weight
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(objective);
    }

    public async Task<StrategicObjectiveDto> UpdateObjectiveAsync(
        Guid objectiveId,
        UpdateStrategicObjectiveRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureEditor();
        var objective = await db.StrategicObjectives.SingleOrDefaultAsync(
            x => x.Id == objectiveId && x.TenantId == user.TenantId,
            cancellationToken) ?? throw new KeyNotFoundException("Strategic objective was not found.");

        var name = NormalizeRequired(request.Name, 240, "Objective name");
        ValidateWeight(request.Weight, "Objective weight");
        await ValidateParentAsync(objectiveId, request.ParentId, cancellationToken);

        var old = new
        {
            objective.ParentId,
            objective.Name,
            objective.Description,
            objective.Weight,
            objective.IsActive
        };
        objective.ParentId = request.ParentId;
        objective.Name = name;
        objective.Description = NormalizeOptional(request.Description, 2000);
        objective.Weight = request.Weight;
        objective.IsActive = request.IsActive;
        objective.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("StrategicObjective", objective.Id, "UPDATE", old, new
        {
            objective.ParentId,
            objective.Name,
            objective.Description,
            objective.Weight,
            objective.IsActive
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(objective);
    }

    public async Task<IReadOnlyList<KpiObjectiveLinkDto>> GetKpiLinksAsync(
        CancellationToken cancellationToken = default) =>
        await db.KpiObjectiveLinks.AsNoTracking()
            .Where(x => x.Kpi!.TenantId == user.TenantId && x.Objective!.TenantId == user.TenantId)
            .OrderBy(x => x.Objective!.Code)
            .ThenBy(x => x.Kpi!.Code)
            .Select(x => new KpiObjectiveLinkDto(
                x.KpiId,
                x.Kpi!.Code,
                x.Kpi.Name,
                x.ObjectiveId,
                x.Objective!.Code,
                x.Objective.Name,
                x.Weight))
            .ToListAsync(cancellationToken);

    public async Task<KpiObjectiveLinkDto> UpsertKpiLinkAsync(
        UpsertKpiObjectiveLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureEditor();
        ValidateLinkWeight(request.Weight);
        var kpi = await db.Kpis.SingleOrDefaultAsync(
            x => x.Id == request.KpiId && x.TenantId == user.TenantId,
            cancellationToken) ?? throw new ArgumentException("KPI is invalid for the current tenant.");
        var objective = await db.StrategicObjectives.SingleOrDefaultAsync(
            x => x.Id == request.ObjectiveId && x.TenantId == user.TenantId && x.IsActive,
            cancellationToken) ?? throw new ArgumentException("Strategic objective is invalid or inactive.");

        var link = await db.KpiObjectiveLinks.SingleOrDefaultAsync(
            x => x.KpiId == request.KpiId && x.ObjectiveId == request.ObjectiveId,
            cancellationToken);
        var oldWeight = link?.Weight;
        if (link is null)
        {
            link = new KpiObjectiveLink
            {
                KpiId = request.KpiId,
                ObjectiveId = request.ObjectiveId,
                Weight = request.Weight
            };
            db.KpiObjectiveLinks.Add(link);
        }
        else
        {
            link.Weight = request.Weight;
        }

        AddAudit("KpiObjectiveLink", $"{request.KpiId:N}:{request.ObjectiveId:N}", oldWeight.HasValue ? "UPDATE" : "CREATE",
            oldWeight.HasValue ? new { Weight = oldWeight.Value } : null,
            new { request.Weight, Kpi = kpi.Code, Objective = objective.Code });
        await db.SaveChangesAsync(cancellationToken);
        return new KpiObjectiveLinkDto(
            kpi.Id,
            kpi.Code,
            kpi.Name,
            objective.Id,
            objective.Code,
            objective.Name,
            link.Weight);
    }

    public async Task DeleteKpiLinkAsync(
        Guid kpiId,
        Guid objectiveId,
        CancellationToken cancellationToken = default)
    {
        EnsureEditor();
        var link = await db.KpiObjectiveLinks
            .Include(x => x.Kpi)
            .Include(x => x.Objective)
            .SingleOrDefaultAsync(
                x => x.KpiId == kpiId
                    && x.ObjectiveId == objectiveId
                    && x.Kpi!.TenantId == user.TenantId
                    && x.Objective!.TenantId == user.TenantId,
                cancellationToken) ?? throw new KeyNotFoundException("KPI/objective link was not found.");

        var old = new
        {
            link.Weight,
            Kpi = link.Kpi!.Code,
            Objective = link.Objective!.Code
        };
        db.KpiObjectiveLinks.Remove(link);
        AddAudit("KpiObjectiveLink", $"{kpiId:N}:{objectiveId:N}", "DELETE", old, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateParentAsync(
        Guid? objectiveId,
        Guid? parentId,
        CancellationToken cancellationToken)
    {
        if (!parentId.HasValue) return;
        if (objectiveId.HasValue && objectiveId.Value == parentId.Value)
            throw new ArgumentException("A strategic objective cannot be its own parent.");

        var nodes = await db.StrategicObjectives.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId)
            .Select(x => new { x.Id, x.ParentId })
            .ToListAsync(cancellationToken);
        var byId = nodes.ToDictionary(x => x.Id);
        if (!byId.ContainsKey(parentId.Value))
            throw new ArgumentException("Parent strategic objective was not found in the current tenant.");

        if (!objectiveId.HasValue) return;
        var current = parentId;
        var visited = new HashSet<Guid>();
        while (current.HasValue)
        {
            if (!visited.Add(current.Value))
                throw new InvalidOperationException("The strategic objective hierarchy already contains a cycle.");
            if (current.Value == objectiveId.Value)
                throw new InvalidOperationException("The selected parent would create a strategic objective hierarchy cycle.");
            current = byId.TryGetValue(current.Value, out var node) ? node.ParentId : null;
        }
    }

    private void EnsureEditor()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("BUDGET_MANAGER")) return;
        throw new UnauthorizedAccessException("Budget manager or administrator role is required to maintain strategy mapping.");
    }

    private static void ValidateWeight(decimal weight, string field)
    {
        if (weight is < 0m or > 100m)
            throw new ArgumentException($"{field} must be between 0 and 100.");
    }

    private static void ValidateLinkWeight(decimal weight)
    {
        if (weight is <= 0m or > 100m)
            throw new ArgumentException("KPI/objective link weight must be greater than 0 and at most 100.");
    }

    private static StrategicObjectiveDto ToDto(StrategicObjective objective) => new(
        objective.Id,
        objective.ParentId,
        objective.Code,
        objective.Name,
        objective.Description,
        objective.Weight,
        objective.IsActive);

    private void AddAudit(string entityType, Guid entityId, string action, object? oldValue, object? newValue) =>
        AddAudit(entityType, entityId.ToString(), action, oldValue, newValue);

    private void AddAudit(string entityType, string entityId, string action, object? oldValue, object? newValue) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue)
        });

    private static string NormalizeRequired(string? value, int maxLength, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException($"{field} is required.");
        if (normalized.Length > maxLength) throw new ArgumentException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > maxLength) throw new ArgumentException($"Value cannot exceed {maxLength} characters.");
        return normalized;
    }
}
