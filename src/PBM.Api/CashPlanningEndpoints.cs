using PBM.Application;

namespace PBM.Api;

public static class CashPlanningEndpoints
{
    public static RouteGroupBuilder MapCashPlanningEndpoints(this RouteGroupBuilder api)
    {
        var cash = api.MapGroup("/cash-planning");

        cash.MapGet("/setup", (
            Guid companyId,
            Guid fiscalYearId,
            ICashPlanningService service,
            CancellationToken ct) =>
            service.GetSetupAsync(companyId, fiscalYearId, ct));

        cash.MapPost("/ensure-plan", (
            EnsureCashPlanRequest request,
            ICashPlanningService service,
            CancellationToken ct) =>
            service.EnsurePlanAsync(request, ct));

        cash.MapGet("/summary", (
            Guid versionId,
            string? currencyCode,
            ICashPlanningService service,
            CancellationToken ct) =>
            service.GetSummaryAsync(versionId, currencyCode, ct));

        cash.MapGet("/entries", (
            Guid versionId,
            string? currencyCode,
            Guid? periodId,
            ICashPlanningService service,
            CancellationToken ct) =>
            service.GetEntriesAsync(versionId, currencyCode, periodId, ct));

        cash.MapPut("/entries", async (
            UpsertCashPlanEntryRequest request,
            ICashPlanningService service,
            CancellationToken ct) =>
            Results.Ok(new { id = await service.UpsertEntryAsync(request, ct) }));

        return api;
    }
}
