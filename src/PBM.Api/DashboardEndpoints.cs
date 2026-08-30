using PBM.Application;

namespace PBM.Api;

public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardAnalyticsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard/metrics", (
            Guid companyId,
            Guid fiscalYearId,
            IDashboardAnalyticsService service,
            CancellationToken ct) =>
            service.GetMetricOptionsAsync(companyId, fiscalYearId, ct));

        api.MapGet("/dashboard/summary-by-measure", (
            Guid companyId,
            Guid fiscalYearId,
            string measureCode,
            IDashboardAnalyticsService service,
            CancellationToken ct) =>
            service.GetSummaryForMeasureAsync(companyId, fiscalYearId, measureCode, ct));

        api.MapGet("/dashboard/drilldown/dimensions", (
            Guid companyId,
            Guid fiscalYearId,
            string measureCode,
            IDashboardAnalyticsService service,
            CancellationToken ct) =>
            service.GetDrilldownDimensionsAsync(companyId, fiscalYearId, measureCode, ct));

        api.MapGet("/dashboard/drilldown", (
            Guid companyId,
            Guid fiscalYearId,
            string measureCode,
            Guid dimensionId,
            int? take,
            IDashboardAnalyticsService service,
            CancellationToken ct) =>
            service.GetDrilldownAsync(companyId, fiscalYearId, measureCode, dimensionId, take ?? 50, ct));

        api.MapGet("/dashboard/purchase", (
            Guid companyId,
            Guid fiscalYearId,
            Guid? dimensionId,
            int? take,
            IPurchaseDashboardService service,
            CancellationToken ct) =>
            service.GetAsync(companyId, fiscalYearId, dimensionId, take ?? 50, ct));

        return api;
    }
}
