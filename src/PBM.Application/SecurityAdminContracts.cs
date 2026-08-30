namespace PBM.Application;

public sealed record SecurityRoleDto(Guid Id, string Code, string Name);
public sealed record UserCompanyAccessDto(
    Guid CompanyId,
    string CompanyCode,
    string CompanyName,
    Guid? OrganizationUnitId,
    string? OrganizationUnitCode,
    string? OrganizationUnitName,
    bool CanRead,
    bool CanWrite);
public sealed record SecurityUserDto(
    Guid Id,
    string UserName,
    string DisplayName,
    string? Email,
    bool IsActive,
    IReadOnlyList<SecurityRoleDto> Roles,
    IReadOnlyList<UserCompanyAccessDto> CompanyAccess);

public sealed record UserCompanyAccessInput(Guid CompanyId, Guid? OrganizationUnitId, bool CanRead, bool CanWrite)
{
    // Compatibility overload for integration/service-account code paths that do not
    // belong to an organizational position. Human users can still supply the full
    // four-argument shape and bind their company access to an OrganizationUnit/Position.
    public UserCompanyAccessInput(Guid companyId, bool canRead, bool canWrite)
        : this(companyId, null, canRead, canWrite)
    {
    }
}

public sealed record CreateSecurityUserRequest(
    string UserName,
    string DisplayName,
    string? Email,
    string Password,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<UserCompanyAccessInput> CompanyAccess);

public sealed record UpdateSecurityUserRequest(
    string DisplayName,
    string? Email,
    bool IsActive,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<UserCompanyAccessInput> CompanyAccess);

public sealed record ChangeUserPasswordRequest(string NewPassword);
public sealed record CreateSecurityRoleRequest(string Code, string Name);
public sealed record LicenseUsageDto(int MaxUsers, int ActiveUsers, int MaxCompanies, int ActiveCompanies, DateTime ExpiresAtUtc, bool IsActive);

public interface ISecurityAdminService
{
    Task<IReadOnlyList<SecurityUserDto>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SecurityRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<LicenseUsageDto> GetLicenseUsageAsync(CancellationToken cancellationToken = default);
    Task<SecurityUserDto> CreateUserAsync(CreateSecurityUserRequest request, CancellationToken cancellationToken = default);
    Task<SecurityUserDto> UpdateUserAsync(Guid userId, UpdateSecurityUserRequest request, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default);
    Task<SecurityRoleDto> CreateRoleAsync(CreateSecurityRoleRequest request, CancellationToken cancellationToken = default);
}
