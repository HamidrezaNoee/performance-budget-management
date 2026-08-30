using PBM.Application;
using PBM.Domain;

namespace PBM.Api;

public static class FinancialReportEndpoints
{
    public static RouteGroupBuilder MapFinancialReportEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/reports/financial", (Guid companyId, Guid fiscalYearId, FinancialReportType type, ValueKind? valueKind, Guid? versionId, IFinancialReportService service, CancellationToken ct) =>
            service.GetAsync(companyId, fiscalYearId, type, valueKind ?? ValueKind.Budget, versionId, ct));
        return api;
    }
}
