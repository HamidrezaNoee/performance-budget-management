using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class SecurityAdminService(
    PbmDbContext db,
    IUserContext currentUser,
    IPasswordHasher<AppUser> passwordHasher) : ISecurityAdminService
{
    public async Task<IReadOnlyList<SecurityUserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var users = await db.Users.AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId)
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.CompanyAccess).ThenInclude(x => x.Company)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        return users.Select(MapUser).ToList();
    }

    public async Task<IReadOnlyList<SecurityRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        return await db.Roles.AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId)
            .OrderBy(x => x.Name)
            .Select(x => new SecurityRoleDto(x.Id, x.Code, x.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<LicenseUsageDto> GetLicenseUsageAsync(CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var license = await GetLicenseAsync(cancellationToken);
        var activeUsers = await db.Users.CountAsync(x => x.TenantId == currentUser.TenantId && x.IsActive, cancellationToken);
        var activeCompanies = await db.Companies.CountAsync(x => x.TenantId == currentUser.TenantId && x.IsActive, cancellationToken);
        return new LicenseUsageDto(license.MaxUsers, activeUsers, license.MaxCompanies, activeCompanies, license.ExpiresAtUtc, license.IsActive && license.ExpiresAtUtc >= DateTime.UtcNow);
    }

    public async Task<SecurityUserDto> CreateUserAsync(CreateSecurityUserRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        ValidateUserName(request.UserName);
        ValidateDisplayName(request.DisplayName);
        ValidatePassword(request.Password);
        await EnsureLicenseAllowsAnotherUserAsync(cancellationToken);

        var normalizedUserName = request.UserName.Trim();
        if (await db.Users.AnyAsync(x => x.TenantId == currentUser.TenantId && x.UserName == normalizedUserName, cancellationToken))
            throw new InvalidOperationException("A user with this username already exists.");

        var roles = await ResolveRolesAsync(request.RoleIds, cancellationToken);
        var companies = await ResolveCompanyAccessAsync(request.CompanyAccess, cancellationToken);
        var user = new AppUser
        {
            TenantId = currentUser.TenantId,
            UserName = normalizedUserName,
            DisplayName = request.DisplayName.Trim(),
            Email = NormalizeOptional(request.Email),
            PasswordHash = "pending",
            IsActive = true
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        foreach (var role in roles) user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        foreach (var item in companies)
            user.CompanyAccess.Add(new UserCompanyAccess { UserId = user.Id, CompanyId = item.Company.Id, CanRead = item.Input.CanRead, CanWrite = item.Input.CanWrite });

        db.Users.Add(user);
        AddAudit("AppUser", user.Id, "CREATE", new { user.UserName, user.DisplayName, user.Email, Roles = roles.Select(x => x.Code), Companies = companies.Select(x => new { x.Company.Code, x.Input.CanRead, x.Input.CanWrite }) });
        await db.SaveChangesAsync(cancellationToken);
        return await GetUserAsync(user.Id, cancellationToken);
    }

    public async Task<SecurityUserDto> UpdateUserAsync(Guid userId, UpdateSecurityUserRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        ValidateDisplayName(request.DisplayName);
        var user = await db.Users
            .Include(x => x.UserRoles)
            .Include(x => x.CompanyAccess)
            .SingleOrDefaultAsync(x => x.Id == userId && x.TenantId == currentUser.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("User was not found.");

        if (user.Id == currentUser.UserId && !request.IsActive)
            throw new InvalidOperationException("You cannot deactivate your own account.");
        if (!user.IsActive && request.IsActive) await EnsureLicenseAllowsAnotherUserAsync(cancellationToken);

        var roles = await ResolveRolesAsync(request.RoleIds, cancellationToken);
        if (user.Id == currentUser.UserId && currentUser.IsInRole("SUPERADMIN") && roles.All(x => !string.Equals(x.Code, "SUPERADMIN", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("You cannot remove your own SUPERADMIN role.");
        var companies = await ResolveCompanyAccessAsync(request.CompanyAccess, cancellationToken);

        var oldRoleIds = user.UserRoles.Select(x => x.RoleId).OrderBy(x => x).ToArray();
        var newRoleIds = roles.Select(x => x.Id).OrderBy(x => x).ToArray();
        var oldCompanyAccess = user.CompanyAccess.Select(x => (x.CompanyId, x.CanRead, x.CanWrite)).OrderBy(x => x.CompanyId).ToArray();
        var newCompanyAccess = companies.Select(x => (x.Company.Id, x.Input.CanRead, x.Input.CanWrite)).OrderBy(x => x.Id).ToArray();
        var authorizationChanged = user.IsActive != request.IsActive
            || !oldRoleIds.SequenceEqual(newRoleIds)
            || !oldCompanyAccess.SequenceEqual(newCompanyAccess);

        var old = new
        {
            user.DisplayName,
            user.Email,
            user.IsActive,
            user.TokenVersion,
            RoleIds = user.UserRoles.Select(x => x.RoleId).ToArray(),
            CompanyIds = user.CompanyAccess.Select(x => new { x.CompanyId, x.CanRead, x.CanWrite }).ToArray()
        };

        user.DisplayName = request.DisplayName.Trim();
        user.Email = NormalizeOptional(request.Email);
        user.IsActive = request.IsActive;
        if (authorizationChanged) user.TokenVersion++;
        user.UpdatedAtUtc = DateTime.UtcNow;
        db.UserRoles.RemoveRange(user.UserRoles);
        db.UserCompanyAccess.RemoveRange(user.CompanyAccess);
        user.UserRoles.Clear();
        user.CompanyAccess.Clear();
        foreach (var role in roles) user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        foreach (var item in companies)
            user.CompanyAccess.Add(new UserCompanyAccess { UserId = user.Id, CompanyId = item.Company.Id, CanRead = item.Input.CanRead, CanWrite = item.Input.CanWrite });

        AddAudit("AppUser", user.Id, "UPDATE", new
        {
            user.DisplayName,
            user.Email,
            user.IsActive,
            user.TokenVersion,
            AuthorizationChanged = authorizationChanged,
            Roles = roles.Select(x => x.Code),
            Companies = companies.Select(x => new { x.Company.Code, x.Input.CanRead, x.Input.CanWrite })
        }, JsonSerializer.Serialize(old));
        await db.SaveChangesAsync(cancellationToken);
        return await GetUserAsync(user.Id, cancellationToken);
    }

    public async Task ChangePasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        ValidatePassword(newPassword);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.TenantId == currentUser.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("User was not found.");
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.TokenVersion++;
        user.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("AppUser", user.Id, "PASSWORD_RESET", new { ResetBy = currentUser.UserId, user.TokenVersion });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SecurityRoleDto> CreateRoleAsync(CreateSecurityRoleRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (request.Name ?? string.Empty).Trim();
        if (code.Length is < 2 or > 64 || code.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
            throw new ArgumentException("Role code must contain 2-64 letters, numbers, underscore or dash characters.");
        if (name.Length is < 2 or > 128) throw new ArgumentException("Role name is required and must be at most 128 characters.");
        if (await db.Roles.AnyAsync(x => x.TenantId == currentUser.TenantId && x.Code == code, cancellationToken))
            throw new InvalidOperationException("A role with this code already exists.");

        var role = new Role { TenantId = currentUser.TenantId, Code = code, Name = name };
        db.Roles.Add(role);
        AddAudit("Role", role.Id, "CREATE", new { role.Code, role.Name });
        await db.SaveChangesAsync(cancellationToken);
        return new SecurityRoleDto(role.Id, role.Code, role.Name);
    }

    private async Task<SecurityUserDto> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId && x.TenantId == currentUser.TenantId)
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.CompanyAccess).ThenInclude(x => x.Company)
            .SingleAsync(ct);
        return MapUser(user);
    }

    private static SecurityUserDto MapUser(AppUser user) => new(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.Email,
        user.IsActive,
        user.UserRoles.Where(x => x.Role is not null).Select(x => new SecurityRoleDto(x.RoleId, x.Role!.Code, x.Role.Name)).OrderBy(x => x.Name).ToList(),
        user.CompanyAccess.Where(x => x.Company is not null).Select(x => new UserCompanyAccessDto(x.CompanyId, x.Company!.Code, x.Company.Name, x.CanRead, x.CanWrite)).OrderBy(x => x.CompanyName).ToList());

    private async Task<IReadOnlyList<Role>> ResolveRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        var ids = (roleIds ?? []).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var roles = await db.Roles.Where(x => x.TenantId == currentUser.TenantId && ids.Contains(x.Id)).ToListAsync(ct);
        if (roles.Count != ids.Length) throw new ArgumentException("One or more selected roles are invalid.");
        return roles;
    }

    private async Task<IReadOnlyList<(Company Company, UserCompanyAccessInput Input)>> ResolveCompanyAccessAsync(IReadOnlyList<UserCompanyAccessInput> inputs, CancellationToken ct)
    {
        var normalized = (inputs ?? []).GroupBy(x => x.CompanyId).Select(g => g.Last()).ToArray();
        var ids = normalized.Select(x => x.CompanyId).ToArray();
        var companies = await db.Companies.Where(x => x.TenantId == currentUser.TenantId && ids.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        if (companies.Count != ids.Length) throw new ArgumentException("One or more selected companies are invalid.");
        return normalized.Select(x => (companies[x.CompanyId], new UserCompanyAccessInput(x.CompanyId, x.CanRead || x.CanWrite, x.CanWrite))).ToList();
    }

    private async Task EnsureLicenseAllowsAnotherUserAsync(CancellationToken ct)
    {
        var license = await GetLicenseAsync(ct);
        if (!license.IsActive || license.StartsAtUtc > DateTime.UtcNow || license.ExpiresAtUtc < DateTime.UtcNow)
            throw new InvalidOperationException("The tenant license is not active.");
        var activeUsers = await db.Users.CountAsync(x => x.TenantId == currentUser.TenantId && x.IsActive, ct);
        if (activeUsers >= license.MaxUsers) throw new InvalidOperationException($"The license allows a maximum of {license.MaxUsers} active users.");
    }

    private async Task<LicenseSubscription> GetLicenseAsync(CancellationToken ct) =>
        await db.LicenseSubscriptions.AsNoTracking().Where(x => x.TenantId == currentUser.TenantId).OrderByDescending(x => x.ExpiresAtUtc).FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException("No license is configured for this tenant.");

    private void EnsureAdmin()
    {
        if (!currentUser.IsInRole("SUPERADMIN") && !currentUser.IsInRole("ADMIN"))
            throw new UnauthorizedAccessException("Administrator role is required.");
    }

    private static void ValidateUserName(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length is < 3 or > 80 || text.Any(char.IsWhiteSpace)) throw new ArgumentException("Username must contain 3-80 characters without spaces.");
    }

    private static void ValidateDisplayName(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length is < 2 or > 160) throw new ArgumentException("Display name is required and must be at most 160 characters.");
    }

    private static void ValidatePassword(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length < 10 || !text.Any(char.IsUpper) || !text.Any(char.IsLower) || !text.Any(char.IsDigit))
            throw new ArgumentException("Password must be at least 10 characters and include uppercase, lowercase and a number.");
    }

    private void AddAudit(string entityType, Guid entityId, string action, object newValue, string? oldValueJson = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = currentUser.TenantId,
            UserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Action = action,
            OldValueJson = oldValueJson,
            NewValueJson = JsonSerializer.Serialize(newValue)
        });

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
