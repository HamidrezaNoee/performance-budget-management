using PBM.Domain;

namespace PBM.Application;

public sealed record BudgetVersionDetailsDto(
    Guid Id,
    Guid BudgetPlanId,
    Guid ScenarioId,
    int VersionNumber,
    string Name,
    BudgetStatus Status,
    bool IsLocked,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateBudgetRevisionRequest(Guid SourceVersionId, string Name, Guid? ScenarioId = null);
public sealed record ChangeBudgetVersionStatusRequest(BudgetStatus Status, string? Comment);
public sealed record AddBudgetCommentRequest(string Text);
public sealed record BudgetCommentDto(Guid Id, Guid VersionId, Guid UserId, string UserDisplayName, string Text, DateTime CreatedAtUtc);

public interface IBudgetWorkflowService
{
    Task<BudgetVersionDetailsDto> CreateRevisionAsync(CreateBudgetRevisionRequest request, CancellationToken cancellationToken = default);
    Task<BudgetVersionDetailsDto> ChangeStatusAsync(Guid versionId, ChangeBudgetVersionStatusRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BudgetCommentDto>> GetCommentsAsync(Guid versionId, CancellationToken cancellationToken = default);
    Task<BudgetCommentDto> AddCommentAsync(Guid versionId, string text, CancellationToken cancellationToken = default);
}
