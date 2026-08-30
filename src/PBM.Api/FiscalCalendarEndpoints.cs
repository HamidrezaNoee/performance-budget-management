using PBM.Application;

namespace PBM.Api;

public static class FiscalCalendarEndpoints
{
    public static RouteGroupBuilder MapFiscalCalendarEndpoints(this RouteGroupBuilder api)
    {
        var calendar = api.MapGroup("/admin/fiscal-calendar");

        calendar.MapGet("/years", (Guid companyId, IFiscalCalendarService service, CancellationToken ct) =>
            service.GetYearsAsync(companyId, ct));

        calendar.MapPost("/years", (CreateFiscalYearRequest request, IFiscalCalendarService service, CancellationToken ct) =>
            service.CreateYearAsync(request, ct));

        calendar.MapPost("/years/{fiscalYearId:guid}/periods", (Guid fiscalYearId, CreateFiscalPeriodRequest request, IFiscalCalendarService service, CancellationToken ct) =>
            service.AddPeriodAsync(fiscalYearId, request, ct));

        calendar.MapPut("/years/{fiscalYearId:guid}/closed", (Guid fiscalYearId, SetFiscalYearClosedRequest request, IFiscalCalendarService service, CancellationToken ct) =>
            service.SetYearClosedAsync(fiscalYearId, request.IsClosed, ct));

        calendar.MapPut("/periods/{periodId:guid}/closed", (Guid periodId, SetFiscalPeriodClosedRequest request, IFiscalCalendarService service, CancellationToken ct) =>
            service.SetPeriodClosedAsync(periodId, request.IsClosed, ct));

        return api;
    }
}
