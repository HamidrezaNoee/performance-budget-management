using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class NotifyingBudgetWorkflowService(
    BudgetWorkflowService inner,
    PbmDbContext db,
    IUserContext currentUser,
    INotificationService notifications) : IBudgetWorkflowService
{
    public Task<BudgetVersionDetailsDto> CreateRevisionAsync(
        CreateBudgetRevisionRequest request,
        CancellationToken cancellationToken = default) =>
        inner.CreateRevisionAsync(request, cancellationToken);

    public async Task<BudgetVersionDetailsDto> ChangeStatusAsync(
        Guid versionId,
        ChangeBudgetVersionStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new WorkflowContext(
                x.Status,
                x.VersionNumber,
                x.Name,
                x.BudgetPlan!.Id,
                x.BudgetPlan.CompanyId,
                x.BudgetPlan.Name,
                x.BudgetPlan.Company!.Name))
            .SingleAsync(cancellationToken);

        var result = await inner.ChangeStatusAsync(versionId, request, cancellationToken);
        if (context.OldStatus == result.Status) return result;

        var recipients = await ResolveRecipientsAsync(context.CompanyId, result.Status, cancellationToken);
        recipients.Remove(currentUser.UserId);
        if (recipients.Count == 0) return result;

        var presentation = DescribeTransition(context, result.Status);
        await notifications.DispatchAsync(new NotificationDispatchRequest(
            recipients,
            context.CompanyId,
            "BUDGET_WORKFLOW",
            presentation.Title,
            presentation.Message,
            presentation.Severity,
            "BudgetVersion",
            result.Id.ToString()), cancellationToken);

        return result;
    }

    public Task<IReadOnlyList<BudgetCommentDto>> GetCommentsAsync(
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        inner.GetCommentsAsync(versionId, cancellationToken);

    public Task<BudgetCommentDto> AddCommentAsync(
        Guid versionId,
        string text,
        CancellationToken cancellationToken = default) =>
        inner.AddCommentAsync(versionId, text, cancellationToken);

    private async Task<HashSet<Guid>> ResolveRecipientsAsync(
        Guid companyId,
        BudgetStatus targetStatus,
        CancellationToken cancellationToken)
    {
        string[] roleCodes = targetStatus switch
        {
            BudgetStatus.Submitted => ["BUDGET_MANAGER", "CFO", "ADMIN", "SUPERADMIN"],
            BudgetStatus.UnderReview => ["BUDGET_MANAGER", "CFO", "CEO", "ADMIN", "SUPERADMIN"],
            BudgetStatus.Approved => ["CFO", "CEO", "BUDGET_MANAGER", "ADMIN", "SUPERADMIN"],
            BudgetStatus.Rejected => ["BUDGET_MANAGER", "CFO", "ADMIN", "SUPERADMIN"],
            BudgetStatus.Returned => ["BUDGET_MANAGER", "CFO", "ADMIN", "SUPERADMIN"],
            BudgetStatus.Closed => ["CFO", "CEO", "BUDGET_MANAGER", "ADMIN", "SUPERADMIN"],
            _ => []
        };

        var recipientIds = new HashSet<Guid>();
        if (roleCodes.Length > 0)
        {
            var roleRecipients = await db.Users.AsNoTracking()
                .Where(x => x.TenantId == currentUser.TenantId
                    && x.IsActive
                    && x.UserRoles.Any(r => roleCodes.Contains(r.Role!.Code))
                    && (x.UserRoles.Any(r => r.Role!.Code == "SUPERADMIN" || r.Role.Code == "ADMIN")
                        || x.CompanyAccess.Any(a => a.CompanyId == companyId && a.CanRead)))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            recipientIds.UnionWith(roleRecipients);
        }

        if (targetStatus is BudgetStatus.Returned or BudgetStatus.Rejected or BudgetStatus.Approved or BudgetStatus.Closed)
        {
            var companyWriters = await db.UserCompanyAccess.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.CanWrite && x.User!.TenantId == currentUser.TenantId && x.User.IsActive)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);
            recipientIds.UnionWith(companyWriters);
        }

        return recipientIds;
    }

    private static NotificationPresentation DescribeTransition(WorkflowContext context, BudgetStatus targetStatus)
    {
        var versionLabel = $"نسخه {context.VersionNumber} — {context.VersionName}";
        var planLabel = $"{context.PlanName} / {context.CompanyName}";
        return targetStatus switch
        {
            BudgetStatus.Submitted => new(
                "بودجه برای بررسی ارسال شد",
                $"{versionLabel} از «{planLabel}» برای بررسی و تصمیم‌گیری ارسال شده است.",
                NotificationSeverity.Info),
            BudgetStatus.UnderReview => new(
                "بررسی بودجه آغاز شد",
                $"بررسی {versionLabel} از «{planLabel}» آغاز شده است.",
                NotificationSeverity.Info),
            BudgetStatus.Approved => new(
                "بودجه تأیید شد",
                $"{versionLabel} از «{planLabel}» تأیید نهایی شده است.",
                NotificationSeverity.Success),
            BudgetStatus.Returned => new(
                "بودجه برای اصلاح برگشت داده شد",
                $"{versionLabel} از «{planLabel}» برای اصلاح به واحد تهیه‌کننده برگشت داده شده است.",
                NotificationSeverity.Warning),
            BudgetStatus.Rejected => new(
                "بودجه رد شد",
                $"{versionLabel} از «{planLabel}» رد شده است. توضیحات گردش تأیید را بررسی کنید.",
                NotificationSeverity.Error),
            BudgetStatus.Closed => new(
                "نسخه بودجه بسته شد",
                $"{versionLabel} از «{planLabel}» بسته و برای تغییر مستقیم قفل شده است.",
                NotificationSeverity.Success),
            _ => new(
                "وضعیت بودجه تغییر کرد",
                $"وضعیت {versionLabel} از «{planLabel}» تغییر کرده است.",
                NotificationSeverity.Info)
        };
    }

    private sealed record WorkflowContext(
        BudgetStatus OldStatus,
        int VersionNumber,
        string VersionName,
        Guid PlanId,
        Guid CompanyId,
        string PlanName,
        string CompanyName);

    private sealed record NotificationPresentation(
        string Title,
        string Message,
        NotificationSeverity Severity);
}
