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

        api.MapGet("/dashboard/portfolio/sales-dimension", (
            Guid companyId,
            Guid fiscalYearId,
            string dimensionCode,
            int? take,
            IPortfolioDimensionService service,
            CancellationToken ct) =>
            service.GetSalesAsync(companyId, fiscalYearId, dimensionCode, take ?? 50, ct));

        api.MapGet("/dashboard/portfolio/expense-dimension", (
            Guid companyId,
            Guid fiscalYearId,
            string dimensionCode,
            int? take,
            IPortfolioDimensionService service,
            CancellationToken ct) =>
            service.GetExpensesAsync(companyId, fiscalYearId, dimensionCode, take ?? 50, ct));

        return api;
    }
}
