using PBM.Application;

namespace PBM.Api;

public static class MasterDataEndpoints
{
    public static RouteGroupBuilder MapMasterDataEndpoints(this RouteGroupBuilder api)
    {
        var master = api.MapGroup("/master-data");
        master.MapGet("/dimensions", (IMasterDataService service, CancellationToken ct) =>
            service.GetDimensionsAsync(ct));
        master.MapGet("/members", (Guid dimensionId, Guid? companyId, bool? includeInactive, IMasterDataService service, CancellationToken ct) =>
            service.GetMembersAsync(dimensionId, companyId, includeInactive ?? true, ct));
        master.MapPost("/members", (CreateMasterDataMemberRequest request, IMasterDataService service, CancellationToken ct) =>
            service.CreateMemberAsync(request, ct));
        master.MapPut("/members/{memberId:guid}", (Guid memberId, UpdateMasterDataMemberRequest request, IMasterDataService service, CancellationToken ct) =>
            service.UpdateMemberAsync(memberId, request, ct));
        return api;
    }
}
