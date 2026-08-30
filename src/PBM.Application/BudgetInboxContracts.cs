using PBM.Domain;

namespace PBM.Application;

public sealed record BudgetInboxItemDto(
    Guid VersionId,
    Guid BudgetPlanId,
    Guid CompanyId,
    string CompanyName,
    Guid FiscalYearId,
    string FiscalYearName,
    Guid BudgetModelId,
    string BudgetModelName,
    int VersionNumber,
    string VersionName,
    BudgetStatus Status,
    bool IsLocked,
    DateTime UpdatedAtUtc,
    bool CanStartReview,
    bool CanApprove,
    bool CanReturn,
    bool CanReject);

public interface IBudgetInboxService
{
    Task<IReadOnlyList<BudgetInboxItemDto>> GetInboxAsync(Guid? companyId = null, CancellationToken cancellationToken = default);
}
