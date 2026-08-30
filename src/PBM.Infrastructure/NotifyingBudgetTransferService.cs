using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class NotifyingBudgetTransferService(
    BudgetTransferService inner,
    PbmDbContext db,
    IUserContext currentUser,
    INotificationService notifications) : IBudgetTransferService
{
    public Task<IReadOnlyList<BudgetTransferDto>> GetAsync(
        Guid companyId,
        Guid? fiscalYearId = null,
        BudgetTransferStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        inner.GetAsync(companyId, fiscalYearId, status, take, cancellationToken);

    public Task<BudgetTransferAvailabilityDto> GetAvailabilityAsync(
        CreateBudgetTransferRequest request,
        CancellationToken cancellationToken = default) =>
        inner.GetAvailabilityAsync(request, cancellationToken);

    public async Task<BudgetTransferDto> CreateAsync(
        CreateBudgetTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.CreateAsync(request, cancellationToken);
        var reviewers = await ResolveReviewersAsync(result.CompanyId, cancellationToken);
        reviewers.Remove(currentUser.UserId);
        if (reviewers.Count > 0)
        {
            await notifications.DispatchAsync(new NotificationDispatchRequest(
                reviewers,
                result.CompanyId,
                "BUDGET_TRANSFER",
                "درخواست جابجایی بودجه جدید",
                $"درخواست {result.TransferNo} به مبلغ {result.Amount:N0} از {result.SourcePeriodName} به {result.DestinationPeriodName} برای تصمیم‌گیری ثبت شده است.",
                NotificationSeverity.Warning,
                "BudgetTransfer",
                result.Id.ToString(),
                "#transfers"), cancellationToken);
        }
        return result;
    }

    public async Task<BudgetTransferDto> ApproveAsync(
        Guid transferId,
        BudgetTransferDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.ApproveAsync(transferId, request, cancellationToken);
        await NotifyRequesterAsync(result, "جابجایی بودجه تأیید شد", $"درخواست {result.TransferNo} تأیید و مبلغ بودجه بین مبدأ و مقصد منتقل شد.", NotificationSeverity.Success, cancellationToken);
        return result;
    }

    public async Task<BudgetTransferDto> RejectAsync(
        Guid transferId,
        BudgetTransferDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.RejectAsync(transferId, request, cancellationToken);
        await NotifyRequesterAsync(result, "درخواست جابجایی بودجه رد شد", $"درخواست {result.TransferNo} رد شده است. توضیحات تصمیم را بررسی کنید.", NotificationSeverity.Error, cancellationToken);
        return result;
    }

    private async Task<HashSet<Guid>> ResolveReviewersAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var roleCodes = new[] { "CFO", "CEO", "ADMIN", "SUPERADMIN" };
        var ids = await db.Users.AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId
                && x.IsActive
                && x.UserRoles.Any(r => roleCodes.Contains(r.Role!.Code))
                && (x.UserRoles.Any(r => r.Role!.Code == "SUPERADMIN" || r.Role.Code == "ADMIN")
                    || x.CompanyAccess.Any(a => a.CompanyId == companyId && a.CanRead)))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    private Task NotifyRequesterAsync(
        BudgetTransferDto transfer,
        string title,
        string message,
        NotificationSeverity severity,
        CancellationToken cancellationToken)
    {
        if (transfer.RequestedByUserId == currentUser.UserId) return Task.CompletedTask;
        return notifications.DispatchAsync(new NotificationDispatchRequest(
            [transfer.RequestedByUserId],
            transfer.CompanyId,
            "BUDGET_TRANSFER",
            title,
            message,
            severity,
            "BudgetTransfer",
            transfer.Id.ToString(),
            "#transfers"), cancellationToken);
    }
}
