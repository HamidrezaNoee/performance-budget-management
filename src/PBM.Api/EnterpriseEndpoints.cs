using PBM.Application;

namespace PBM.Api;

public static class EnterpriseEndpoints
{
    public static RouteGroupBuilder MapEnterpriseEndpoints(this RouteGroupBuilder api)
    {
        var reference = api.MapGroup("/reference");
        reference.MapGet("/currencies", (IReferenceDataService service, CancellationToken ct) => service.GetCurrenciesAsync(ct));
        reference.MapGet("/currency-catalog", (IReferenceDataService service, CancellationToken ct) => service.GetCurrencyCatalogAsync(ct));
        reference.MapPost("/currencies", (UpsertCurrencyRequest request, IReferenceDataService service, CancellationToken ct) => service.UpsertCurrencyAsync(request, ct));
        reference.MapGet("/fx-rate-sources", (IReferenceDataService service, CancellationToken ct) => service.GetFxRateSourcesAsync(ct));
        reference.MapGet("/fx-rates", (DateTime? fromDate, DateTime? toDate, IReferenceDataService service, CancellationToken ct) => service.GetFxRatesAsync(fromDate, toDate, ct));
        reference.MapPost("/fx-rates", (UpsertFxRateRequest request, IReferenceDataService service, CancellationToken ct) => service.UpsertFxRateAsync(request, ct));
        reference.MapGet("/fx-convert", async (decimal amount, string from, string to, DateTime rateDate, Guid? sourceId, IReferenceDataService service, CancellationToken ct) =>
            Results.Ok(new { amount, from, to, rateDate, convertedAmount = await service.ConvertAsync(amount, from, to, rateDate, sourceId, ct) }));

        var performance = api.MapGroup("/performance");
        performance.MapGet("/kpis", (IKpiService service, CancellationToken ct) => service.GetKpisAsync(ct));
        performance.MapPost("/kpis", (CreateKpiRequest request, IKpiService service, CancellationToken ct) => service.CreateKpiAsync(request, ct));
        performance.MapGet("/kpi-values", (Guid companyId, Guid fiscalYearId, IKpiService service, CancellationToken ct) => service.GetValuesAsync(companyId, fiscalYearId, ct));
        performance.MapPost("/kpi-values", (UpsertKpiValueRequest request, IKpiService service, CancellationToken ct) => service.UpsertValueAsync(request, ct));

        api.MapGet("/audit/recent", (int? take, IAuditService service, CancellationToken ct) => service.GetRecentAsync(take ?? 100, ct));
        return api;
    }
}
