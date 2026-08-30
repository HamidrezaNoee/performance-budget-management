using PBM.Domain;

namespace PBM.Application;

public sealed record CapexOwnerUnitDto(Guid Id, Guid? ParentId, string Code, string Name, string UnitType);

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
    decimal? RequestedBudget,
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
    decimal? RequestedBudget,
    string CurrencyCode,
    Guid? OwnerOrganizationUnitId);

public sealed record UpdateCapexProjectRequest(
    string Name,
    string? Description,
    CapexPriority Priority,
    DateTime StartDate,
    DateTime EndDate,
    decimal? RequestedBudget,
    decimal? ApprovedBudgetLimit,
    string CurrencyCode,
    Guid? OwnerOrganizationUnitId,
    decimal CompletionPercent);

public sealed record ChangeCapexProjectStatusRequest(CapexProjectStatus Status, string? Comment);

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
    decimal? RequestedBudget,
    decimal? ApprovedBudgetLimit,
    decimal BudgetVsApprovedLimitVariance,
    IReadOnlyList<CapexMonthlyFinancialDto> Monthly);

public sealed record CapexCurrencyPortfolioDto(
    string CurrencyCode,
    decimal RequestedBudget,
    decimal ApprovedBudgetLimit,
    decimal Budget,
    decimal Actual,
    decimal Commitment,
    decimal Forecast,
    decimal Available);

public sealed record CapexPortfolioProjectDto(
    Guid ProjectId,
    string Code,
    string Name,
    CapexProjectStatus Status,
    CapexPriority Priority,
    string CurrencyCode,
    decimal? RequestedBudget,
    decimal? ApprovedBudgetLimit,
    decimal Budget,
    decimal Actual,
    decimal Commitment,
    decimal Available,
    decimal CompletionPercent,
    bool IsOverdue);

public sealed record CapexPortfolioSummaryDto(
    Guid CompanyId,
    Guid FiscalYearId,
    int ProjectCount,
    int ProposedCount,
    int SubmittedCount,
    int ApprovedCount,
    int InProgressCount,
    int OnHoldCount,
    int CompletedCount,
    int CancelledCount,
    int OverdueCount,
    IReadOnlyList<CapexCurrencyPortfolioDto> ByCurrency,
    IReadOnlyList<CapexPortfolioProjectDto> Projects);

public interface ICapexService
{
    Task<IReadOnlyList<CapexOwnerUnitDto>> GetOwnerUnitsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Owner-unit lookup is provided by the CAPEX service facade.");

    Task<CapexPortfolioSummaryDto> GetPortfolioAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Portfolio summary is provided by the CAPEX service facade.");

    Task<IReadOnlyList<CapexProjectDto>> GetProjectsAsync(Guid companyId, CapexProjectStatus? status = null, CancellationToken cancellationToken = default);
    Task<CapexProjectDto> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<CapexProjectDto> CreateProjectAsync(CreateCapexProjectRequest request, CancellationToken cancellationToken = default);
    Task<CapexProjectDto> UpdateProjectAsync(Guid projectId, UpdateCapexProjectRequest request, CancellationToken cancellationToken = default);
    Task<CapexProjectDto> ChangeStatusAsync(Guid projectId, ChangeCapexProjectStatusRequest request, CancellationToken cancellationToken = default);
    Task<CapexMilestoneDto> UpsertMilestoneAsync(Guid projectId, UpsertCapexMilestoneRequest request, CancellationToken cancellationToken = default);
    Task DeleteMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken cancellationToken = default);
    Task<CapexFinancialSummaryDto> GetFinancialSummaryAsync(Guid projectId, Guid fiscalYearId, CancellationToken cancellationToken = default);
}
