using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class GovernedSecurityAdminService(
    SecurityAdminService inner,
    PbmDbContext db,
    IUserContext currentUser) : ISecurityAdminService
{
    public Task<IReadOnlyList<SecurityUserDto>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        inner.GetUsersAsync(cancellationToken);

    public Task<IReadOnlyList<SecurityRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        inner.GetRolesAsync(cancellationToken);

    public Task<LicenseUsageDto> GetLicenseUsageAsync(CancellationToken cancellationToken = default) =>
        inner.GetLicenseUsageAsync(cancellationToken);

    public async Task<SecurityUserDto> CreateUserAsync(
        CreateSecurityUserRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureNoIntegrationRoleAsync(request.RoleIds, cancellationToken);
        return await inner.CreateUserAsync(request, cancellationToken);
    }

    public async Task<SecurityUserDto> UpdateUserAsync(
        Guid userId,
        UpdateSecurityUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await IsIntegrationAccountAsync(userId, cancellationToken))
            throw new InvalidOperationException(
                "Integration service accounts cannot be edited through human-user administration. Use the integration credential administration API.");
        await EnsureNoIntegrationRoleAsync(request.RoleIds, cancellationToken);
        return await inner.UpdateUserAsync(userId, request, cancellationToken);
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (await IsIntegrationAccountAsync(userId, cancellationToken))
            throw new InvalidOperationException(
                "Integration service-account passwords are intentionally non-recoverable and cannot be reset. Rotate its client credential instead.");
        await inner.ChangePasswordAsync(userId, newPassword, cancellationToken);
    }

    public Task<SecurityRoleDto> CreateRoleAsync(
        CreateSecurityRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(request.Code?.Trim(), "INTEGRATION", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("INTEGRATION is a reserved system role.");
        return inner.CreateRoleAsync(request, cancellationToken);
    }

    private async Task EnsureNoIntegrationRoleAsync(
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        var ids = (roleIds ?? []).Distinct().ToArray();
        if (ids.Length == 0) return;
        var containsIntegration = await db.Roles.AsNoTracking().AnyAsync(x =>
            x.TenantId == currentUser.TenantId
            && ids.Contains(x.Id)
            && x.Code == "INTEGRATION",
            cancellationToken);
        if (containsIntegration)
            throw new InvalidOperationException(
                "The INTEGRATION role can only be assigned through the dedicated service-account administration API.");
    }

    private Task<bool> IsIntegrationAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        db.UserRoles.AsNoTracking().AnyAsync(x =>
            x.UserId == userId
            && x.User!.TenantId == currentUser.TenantId
            && x.Role!.Code == "INTEGRATION",
            cancellationToken);
}
