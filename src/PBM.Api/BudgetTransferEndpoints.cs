using PBM.Application;
using PBM.Domain;

namespace PBM.Api;

public static class BudgetTransferEndpoints
{
    public static RouteGroupBuilder MapBudgetTransferEndpoints(this RouteGroupBuilder api)
    {
        var transfers = api.MapGroup("/transfers");

        transfers.MapGet("/", (
            Guid companyId,
            Guid? fiscalYearId,
            BudgetTransferStatus? status,
            int? take,
            IBudgetTransferService service,
            CancellationToken ct) =>
            service.GetAsync(companyId, fiscalYearId, status, take ?? 100, ct));

        transfers.MapPost("/availability", (
            CreateBudgetTransferRequest request,
            IBudgetTransferService service,
            CancellationToken ct) =>
            service.GetAvailabilityAsync(request, ct));

        transfers.MapPost("/", (
            CreateBudgetTransferRequest request,
            IBudgetTransferService service,
            CancellationToken ct) =>
            service.CreateAsync(request, ct));

        transfers.MapPost("/{transferId:guid}/approve", (
            Guid transferId,
            BudgetTransferDecisionRequest request,
            IBudgetTransferService service,
            CancellationToken ct) =>
            service.ApproveAsync(transferId, request, ct));

        transfers.MapPost("/{transferId:guid}/reject", (
            Guid transferId,
            BudgetTransferDecisionRequest request,
            IBudgetTransferService service,
            CancellationToken ct) =>
            service.RejectAsync(transferId, request, ct));

        return api;
    }
}
