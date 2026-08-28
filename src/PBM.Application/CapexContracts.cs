using PBM.Domain;

namespace PBM.Application;

public sealed record CapexProjectDto(
    Guid Id,
    Guid CompanyId,
    Guid ProjectDimensionMemberId,
    string Code,
    string Name,
    string? Description,
    CapexProjectStatus Status,
    CapexPriority Priority,
    DateTime StartDate,
    DateTime EndDate,
    decimal? ApprovedBudgetLimit,
    string CurrencyCode,
    Guid? OwnerOrganizationUnitId,
    string? OwnerOrganizationUnitName,
    Guid RequestedByUserId,
    string RequestedByDisplayName,
    Guid? ApprovedByUserId,
    string? ApprovedByDisplayName,
    DateTime? ApprovedAtUtc,
    decimal CompletionPercent,
    string? LastDecisionComment,
    bool IsActive,
    IReadOnlyList<CapexMilestoneDto> Milestones);

public sealed record CapexMilestoneDto(
    Guid Id,
    Guid ProjectId,
    string Code,
    string Name,
    DateTime DueDate,
    decimal Weight,
    decimal ProgressPercent,
    bool IsCompleted,
    DateTime? CompletedAtUtc,
    string? Note);

public sealed record CreateCapexProjectRequest(
    Guid CompanyId,
    string Code,
    string Name,
    string? Description,
    CapexPriority Priority,
    DateTime StartDate,
    DateTime EndDate,
    decimal? RequestedBudgetLimit,
    string CurrencyCode,
    Guid? OwnerOrganizationUnitId);

public sealed record UpdateCapexProjectRequest(
    string Name,
    string? Description,
    CapexPriority Priority,
    DateTime StartDate,
    DateTime EndDate,
    decimal? ApprovedBudgetLimit,
    string CurrencyCode,
    Guid? OwnerOrganizationUnitId,
    decimal CompletionPercent);

public sealed record ChangeCapexProjectStatusRequest(
    CapexProjectStatus Status,
    string? Comment);

public sealed record UpsertCapexMilestoneRequest(
    Guid? Id,
    string Code,
    string Name,
    DateTime DueDate,
    decimal Weight,
    decimal ProgressPercent,
    bool IsCompleted,
    string? Note);

public sealed record CapexMonthlyFinancialDto(
    Guid PeriodId,
    string PeriodName,
    int Sequence,
    decimal Budget,
    decimal Actual,
    decimal Commitment,
    decimal Forecast,
    decimal Available);

public sealed record CapexFinancialSummaryDto(
    Guid ProjectId,
    Guid FiscalYearId,
    decimal Budget,
    decimal Actual,
    decimal Commitment,
    decimal Forecast,
    decimal Available,
    decimal? ApprovedBudgetLimit,
    decimal BudgetVsApprovedLimitVariance,
    IReadOnlyList<CapexMonthlyFinancialDto> Monthly);

public interface ICapexService
{
    Task<IReadOnlyList<CapexProjectDto>> GetProjectsAsync(Guid companyId, CapexProjectStatus? status = null, CancellationToken cancellationToken = default);
    Task<CapexProjectDto> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<CapexProjectDto> CreateProjectAsync(CreateCapexProjectRequest request, CancellationToken cancellationToken = default);
    Task<CapexProjectDto> UpdateProjectAsync(Guid projectId, UpdateCapexProjectRequest request, CancellationToken cancellationToken = default);
    Task<CapexProjectDto> ChangeStatusAsync(Guid projectId, ChangeCapexProjectStatusRequest request, CancellationToken cancellationToken = default);
    Task<CapexMilestoneDto> UpsertMilestoneAsync(Guid projectId, UpsertCapexMilestoneRequest request, CancellationToken cancellationToken = default);
    Task DeleteMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken cancellationToken = default);
    Task<CapexFinancialSummaryDto> GetFinancialSummaryAsync(Guid projectId, Guid fiscalYearId, CancellationToken cancellationToken = default);
}
