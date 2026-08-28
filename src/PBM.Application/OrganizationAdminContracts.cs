namespace PBM.Application;

public sealed record AdminCompanyDto(Guid Id, string Code, string Name, string? Industry, bool IsActive, DateTime CreatedAtUtc);
public sealed record OrganizationUnitDto(Guid Id, Guid CompanyId, Guid? ParentId, string Code, string Name, string UnitType, bool IsActive);

public sealed record CreateCompanyRequest(string Code, string Name, string? Industry);
public sealed record UpdateCompanyRequest(string Name, string? Industry, bool IsActive);
public sealed record CreateOrganizationUnitRequest(Guid CompanyId, Guid? ParentId, string Code, string Name, string UnitType);
public sealed record UpdateOrganizationUnitRequest(Guid? ParentId, string Name, string UnitType, bool IsActive);

public interface IOrganizationAdminService
{
    Task<IReadOnlyList<AdminCompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default);
    Task<AdminCompanyDto> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default);
    Task<AdminCompanyDto> UpdateCompanyAsync(Guid companyId, UpdateCompanyRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationUnitDto>> GetUnitsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<OrganizationUnitDto> CreateUnitAsync(CreateOrganizationUnitRequest request, CancellationToken cancellationToken = default);
    Task<OrganizationUnitDto> UpdateUnitAsync(Guid unitId, UpdateOrganizationUnitRequest request, CancellationToken cancellationToken = default);
}
