using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class BudgetInboxService(PbmDbContext db, IUserContext user) : IBudgetInboxService
{
    public async Task<IReadOnlyList<BudgetInboxItemDto>> GetInboxAsync(Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        if (companyId.HasValue) EnsureCompanyRead(companyId.Value);

        var query = db.BudgetVersions.AsNoTracking()
            .Where(x => x.Status == BudgetStatus.Submitted || x.Status == BudgetStatus.UnderReview || x.Status == BudgetStatus.Returned)
            .Where(x => x.BudgetPlan!.Company!.TenantId == user.TenantId && x.BudgetPlan.Company.IsActive);

        if (companyId.HasValue) query = query.Where(x => x.BudgetPlan!.CompanyId == companyId.Value);
        else if (!user.IsInRole("SUPERADMIN")) query = query.Where(x => user.CompanyIds.Contains(x.BudgetPlan!.CompanyId));

        var rows = await query
            .OrderBy(x => x.Status == BudgetStatus.UnderReview ? 0 : x.Status == BudgetStatus.Submitted ? 1 : 2)
            .ThenBy(x => x.UpdatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.BudgetPlanId,
                x.BudgetPlan!.CompanyId,
                CompanyName = x.BudgetPlan.Company!.Name,
                x.BudgetPlan.FiscalYearId,
                FiscalYearName = x.BudgetPlan.FiscalYear!.Name,
                x.BudgetPlan.BudgetModelId,
                BudgetModelName = x.BudgetPlan.BudgetModel!.Name,
                x.VersionNumber,
                VersionName = x.Name,
                x.Status,
                x.IsLocked,
                x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new BudgetInboxItemDto(
            x.Id,
            x.BudgetPlanId,
            x.CompanyId,
            x.CompanyName,
            x.FiscalYearId,
            x.FiscalYearName,
            x.BudgetModelId,
            x.BudgetModelName,
            x.VersionNumber,
            x.VersionName,
            x.Status,
            x.IsLocked,
            x.UpdatedAtUtc,
            CanStartReview(x.Status, x.CompanyId),
            CanApprove(x.Status, x.CompanyId),
            CanReturn(x.Status, x.CompanyId),
            CanReject(x.Status, x.CompanyId))).ToList();
    }

    private bool IsAdmin => user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN");
    private bool IsReviewManager => IsAdmin || user.IsInRole("BUDGET_MANAGER") || user.IsInRole("CFO");
    private bool IsSeniorReviewer => IsReviewManager || user.IsInRole("CEO");
    private bool IsApprover => IsAdmin || user.IsInRole("CFO") || user.IsInRole("CEO");
    private bool CanWrite(Guid companyId) => user.IsInRole("SUPERADMIN") || user.CanWriteCompany(companyId);

    private bool CanStartReview(BudgetStatus status, Guid companyId) => CanWrite(companyId) && status == BudgetStatus.Submitted && IsReviewManager;
    private bool CanApprove(BudgetStatus status, Guid companyId) => CanWrite(companyId) && status == BudgetStatus.UnderReview && IsApprover;
    private bool CanReturn(BudgetStatus status, Guid companyId) => CanWrite(companyId) && status switch
    {
        BudgetStatus.Submitted => IsReviewManager,
        BudgetStatus.UnderReview => IsSeniorReviewer,
        _ => false
    };
    private bool CanReject(BudgetStatus status, Guid companyId) => CanReturn(status, companyId);

    private void EnsureCompanyRead(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }
}
