using PBM.Application;

namespace PBM.Api;

public static class OrganizationAdminEndpoints
{
    public static RouteGroupBuilder MapOrganizationAdminEndpoints(this RouteGroupBuilder api)
    {
        var organization = api.MapGroup("/admin/organization");

        organization.MapGet("/companies", (IOrganizationAdminService service, CancellationToken ct) => service.GetCompaniesAsync(ct));
        organization.MapPost("/companies", (CreateCompanyRequest request, IOrganizationAdminService service, CancellationToken ct) => service.CreateCompanyAsync(request, ct));
        organization.MapPut("/companies/{companyId:guid}", (Guid companyId, UpdateCompanyRequest request, IOrganizationAdminService service, CancellationToken ct) => service.UpdateCompanyAsync(companyId, request, ct));

        organization.MapGet("/companies/{companyId:guid}/units", (Guid companyId, IOrganizationAdminService service, CancellationToken ct) => service.GetUnitsAsync(companyId, ct));
        organization.MapPost("/units", (CreateOrganizationUnitRequest request, IOrganizationAdminService service, CancellationToken ct) => service.CreateUnitAsync(request, ct));
        organization.MapPut("/units/{unitId:guid}", (Guid unitId, UpdateOrganizationUnitRequest request, IOrganizationAdminService service, CancellationToken ct) => service.UpdateUnitAsync(unitId, request, ct));

        return api;
    }
}
