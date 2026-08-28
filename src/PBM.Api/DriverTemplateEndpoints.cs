using PBM.Application;

namespace PBM.Api;

public static class DriverTemplateEndpoints
{
    public static RouteGroupBuilder MapDriverTemplateEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/driver-templates");

        templates.MapGet("/", (IDriverTemplateService service, CancellationToken ct) =>
            service.GetTemplatesAsync(ct));

        templates.MapPost("/apply", (ApplyDriverTemplateRequest request, IDriverTemplateService service, CancellationToken ct) =>
            service.ApplyAsync(request, ct));

        return api;
    }
}
