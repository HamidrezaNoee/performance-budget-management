using PBM.Application;

namespace PBM.Api;

public static class PortfolioFinancialEndpoints
{
    public static RouteGroupBuilder MapPortfolioFinancialEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard/portfolio/financial-performance", (
            Guid companyId,
            Guid fiscalYearId,
            IPortfolioFinancialService service,
            CancellationToken ct) =>
            service.GetAsync(companyId, fiscalYearId, ct));

        return api;
    }
}
