using PBM.Application;
using PBM.Domain;

namespace PBM.Api;

public static class BudgetReservationEndpoints
{
    public static RouteGroupBuilder MapBudgetReservationEndpoints(this RouteGroupBuilder api)
    {
        var reservations = api.MapGroup("/reservations");

        reservations.MapGet("/", async (
            Guid companyId,
            Guid? fiscalYearId,
            BudgetReservationStatus? status,
            int? take,
            IBudgetReservationService service,
            IBudgetService budgetService,
            CancellationToken ct) =>
        {
            var items = await service.GetAsync(companyId, status, take ?? 100, ct);
            if (!fiscalYearId.HasValue) return Results.Ok(items);

            var plans = await budgetService.GetPlansAsync(companyId, fiscalYearId.Value, ct);
            var versionIds = plans.SelectMany(x => x.Versions).Select(x => x.Id).ToHashSet();
            return Results.Ok(items.Where(x => versionIds.Contains(x.VersionId)).ToList());
        });

        reservations.MapPost("/availability", (
            BudgetAvailabilityRequest request,
            IBudgetReservationService service,
            CancellationToken ct) =>
            service.GetAvailabilityAsync(request.VersionId, request.PeriodId, request.MeasureId, request.Dimensions, ct));

        reservations.MapPost("/", (
            CreateBudgetReservationRequest request,
            IBudgetReservationService service,
            CancellationToken ct) =>
            service.CreateAsync(request, ct));

        reservations.MapPost("/{reservationId:guid}/approve", (
            Guid reservationId,
            BudgetReservationDecisionRequest request,
            IBudgetReservationService service,
            CancellationToken ct) =>
            service.ApproveAsync(reservationId, request, ct));

        reservations.MapPost("/{reservationId:guid}/reject", (
            Guid reservationId,
            BudgetReservationDecisionRequest request,
            IBudgetReservationService service,
            CancellationToken ct) =>
            service.RejectAsync(reservationId, request, ct));

        reservations.MapPost("/{reservationId:guid}/release", (
            Guid reservationId,
            BudgetReservationDecisionRequest request,
            IBudgetReservationService service,
            CancellationToken ct) =>
            service.ReleaseAsync(reservationId, request, ct));

        reservations.MapPost("/{reservationId:guid}/consume", (
            Guid reservationId,
            ConsumeBudgetReservationRequest request,
            IBudgetReservationService service,
            CancellationToken ct) =>
            service.ConsumeAsync(reservationId, request, ct));

        return api;
    }
}

public sealed record BudgetAvailabilityRequest(
    Guid VersionId,
    Guid PeriodId,
    Guid MeasureId,
    IReadOnlyList<DimensionSelection> Dimensions);
