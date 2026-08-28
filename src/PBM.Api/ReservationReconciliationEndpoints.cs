using PBM.Application;

namespace PBM.Api;

public static class ReservationReconciliationEndpoints
{
    public static RouteGroupBuilder MapReservationReconciliationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/budget/reservations/reconciliation", (
            Guid companyId,
            Guid? fiscalYearId,
            int? graceDays,
            decimal? tolerancePercent,
            IReservationReconciliationService service,
            CancellationToken ct) =>
            service.GetAsync(
                companyId,
                fiscalYearId,
                graceDays ?? 2,
                tolerancePercent ?? 0.1m,
                ct));

        return api;
    }
}
