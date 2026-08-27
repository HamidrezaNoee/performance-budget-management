using System.Security.Claims;
using PBM.Application;

namespace PBM.Api;

public sealed class HttpUserContext(IHttpContextAccessor accessor) : IUserContext
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal();
    public Guid UserId => ParseGuid(Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal.FindFirstValue("sub"));
    public Guid TenantId => ParseGuid(Principal.FindFirstValue("tenant_id"));
    public IReadOnlySet<Guid> CompanyIds => Principal.FindAll("company_id").Select(x => Guid.TryParse(x.Value, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).ToHashSet();
    public IReadOnlySet<string> Roles => Principal.FindAll(ClaimTypes.Role).Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public bool IsInRole(string role) => Roles.Contains(role);
    public bool CanAccessCompany(Guid companyId) => CompanyIds.Contains(companyId);

    private static Guid ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : Guid.Empty;
}
