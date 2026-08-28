namespace PBM.Application;

public interface IUserContext
{
    Guid UserId { get; }
    Guid TenantId { get; }
    IReadOnlySet<Guid> CompanyIds { get; }
    IReadOnlySet<Guid> WritableCompanyIds { get; }
    IReadOnlySet<string> Roles { get; }
    bool IsInRole(string role);
    bool CanAccessCompany(Guid companyId);
    bool CanWriteCompany(Guid companyId);
}
