using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class NotifyingBudgetReservationService(
    BudgetReservationService inner,
    PbmDbContext db,
    IUserContext currentUser,
    INotificationService notifications) : IBudgetReservationService
{
    public Task<IReadOnlyList<BudgetReservationDto>> GetAsync(
        Guid companyId,
        BudgetReservationStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        inner.GetAsync(companyId, status, take, cancellationToken);

    public Task<BudgetAvailabilityDto> GetAvailabilityAsync(
        Guid versionId,
        Guid periodId,
        Guid measureId,
        IReadOnlyList<DimensionSelection> dimensions,
        CancellationToken cancellationToken = default) =>
        inner.GetAvailabilityAsync(versionId, periodId, measureId, dimensions, cancellationToken);

    public async Task<BudgetReservationDto> CreateAsync(
        CreateBudgetReservationRequest request,
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
                "BUDGET_RESERVATION",
                "درخواست رزرو بودجه جدید",
                $"درخواست {result.ReservationNo} به مبلغ {result.Amount:N0} برای بررسی و تصمیم‌گیری ثبت شده است.",
                NotificationSeverity.Info,
                "BudgetReservation",
                result.Id.ToString()), cancellationToken);
        }
        return result;
    }

    public async Task<BudgetReservationDto> ApproveAsync(
        Guid reservationId,
        BudgetReservationDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.ApproveAsync(reservationId, request, cancellationToken);
        await NotifyRequesterAsync(result, "رزرو بودجه تأیید شد", $"درخواست {result.ReservationNo} تأیید و مبلغ آن در تعهدات بودجه منظور شد.", NotificationSeverity.Success, cancellationToken);
        return result;
    }

    public async Task<BudgetReservationDto> RejectAsync(
        Guid reservationId,
        BudgetReservationDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.RejectAsync(reservationId, request, cancellationToken);
        await NotifyRequesterAsync(result, "درخواست رزرو بودجه رد شد", $"درخواست {result.ReservationNo} رد شده است. توضیحات تصمیم را بررسی کنید.", NotificationSeverity.Error, cancellationToken);
        return result;
    }

    public async Task<BudgetReservationDto> ReleaseAsync(
        Guid reservationId,
        BudgetReservationDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.ReleaseAsync(reservationId, request, cancellationToken);
        await NotifyRequesterAsync(result, "رزرو بودجه آزاد شد", $"رزرو {result.ReservationNo} آزاد و مبلغ آن از تعهدات بودجه کسر شد.", NotificationSeverity.Warning, cancellationToken);
        return result;
    }

    public async Task<BudgetReservationDto> ConsumeAsync(
        Guid reservationId,
        ConsumeBudgetReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.ConsumeAsync(reservationId, request, cancellationToken);
        await NotifyRequesterAsync(result, "رزرو بودجه مصرف شد", $"رزرو {result.ReservationNo} مصرف‌شده ثبت و از مانده تعهد باز خارج شد.", NotificationSeverity.Success, cancellationToken);
        return result;
    }

    private async Task<HashSet<Guid>> ResolveReviewersAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var roleCodes = new[] { "BUDGET_MANAGER", "CFO", "ADMIN", "SUPERADMIN" };
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
        BudgetReservationDto reservation,
        string title,
        string message,
        NotificationSeverity severity,
        CancellationToken cancellationToken)
    {
        if (reservation.RequestedByUserId == currentUser.UserId) return Task.CompletedTask;
        return notifications.DispatchAsync(new NotificationDispatchRequest(
            [reservation.RequestedByUserId],
            reservation.CompanyId,
            "BUDGET_RESERVATION",
            title,
            message,
            severity,
            "BudgetReservation",
            reservation.Id.ToString()), cancellationToken);
    }
}
