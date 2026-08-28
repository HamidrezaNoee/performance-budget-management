using PBM.Application;
using PBM.Domain;

namespace PBM.Api;

public static class BudgetReservationEndpoints
{
    public static RouteGroupBuilder MapBudgetReservationEndpoints(this RouteGroupBuilder api)
    {
        var reservations = api.MapGroup("/reservations");

        reservations.MapGet("/", (
            Guid companyId,
            BudgetReservationStatus? status,
            int? take,
            IBudgetReservationService service,
            CancellationToken ct) =>
            service.GetAsync(companyId, status, take ?? 100, ct));

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
