using PBM.Application;

namespace PBM.Api;

public static class ActualLedgerEndpoints
{
    public static RouteGroupBuilder MapActualLedgerEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/actual-ledger");

        ledger.MapGet("/entries", (
            Guid companyId,
            Guid fiscalYearId,
            Guid? versionId,
            string? sourceSystem,
            int? take,
            IActualLedgerService service,
            CancellationToken ct) =>
            service.GetEntriesAsync(
                companyId,
                fiscalYearId,
                versionId,
                sourceSystem,
                take ?? 500,
                ct));

        ledger.MapPost("/post", (
            PostActualLedgerRequest request,
            IActualLedgerService service,
            CancellationToken ct) =>
            service.PostAsync(request, ct));

        ledger.MapPost("/post-by-key", (
            PostActualLedgerByKeyRequest request,
            IActualLedgerKeyPostingService service,
            CancellationToken ct) =>
            service.PostAsync(request, ct));

        ledger.MapPost("/batch", (
            PostActualLedgerBatchRequest request,
            IActualLedgerBatchService service,
            CancellationToken ct) =>
            service.PostAsync(request, ct));

        ledger.MapPost("/{entryId:guid}/reverse", (
            Guid entryId,
            ReverseActualLedgerRequest request,
            IActualLedgerService service,
            CancellationToken ct) =>
            service.ReverseAsync(entryId, request, ct));

        ledger.MapGet("/reconciliation", (
            Guid versionId,
            decimal? tolerance,
            IActualLedgerService service,
            CancellationToken ct) =>
            service.ReconcileAsync(versionId, tolerance ?? 0.01m, ct));

        ledger.MapPost("/rebuild-projection", async (
            Guid versionId,
            IActualLedgerService service,
            CancellationToken ct) =>
        {
            var rebuilt = await service.RebuildProjectionAsync(versionId, ct);
            return Results.Ok(new { rebuilt });
        });

        return api;
    }
}
