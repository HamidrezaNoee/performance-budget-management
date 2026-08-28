namespace PBM.Application;

public sealed record StrategicObjectiveDto(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    string? Description,
    decimal Weight,
    bool IsActive);

public sealed record CreateStrategicObjectiveRequest(
    Guid? ParentId,
    string Code,
    string Name,
    string? Description,
    decimal Weight);

public sealed record UpdateStrategicObjectiveRequest(
    Guid? ParentId,
    string Name,
    string? Description,
    decimal Weight,
    bool IsActive);

public sealed record KpiObjectiveLinkDto(
    Guid KpiId,
    string KpiCode,
    string KpiName,
    Guid ObjectiveId,
    string ObjectiveCode,
    string ObjectiveName,
    decimal Weight);

public sealed record UpsertKpiObjectiveLinkRequest(
    Guid KpiId,
    Guid ObjectiveId,
    decimal Weight);

public interface IStrategyService
{
    Task<IReadOnlyList<StrategicObjectiveDto>> GetObjectivesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<StrategicObjectiveDto> CreateObjectiveAsync(
        CreateStrategicObjectiveRequest request,
        CancellationToken cancellationToken = default);

    Task<StrategicObjectiveDto> UpdateObjectiveAsync(
        Guid objectiveId,
        UpdateStrategicObjectiveRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KpiObjectiveLinkDto>> GetKpiLinksAsync(
        CancellationToken cancellationToken = default);

    Task<KpiObjectiveLinkDto> UpsertKpiLinkAsync(
        UpsertKpiObjectiveLinkRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteKpiLinkAsync(
        Guid kpiId,
        Guid objectiveId,
        CancellationToken cancellationToken = default);
}
