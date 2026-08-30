using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class IntegrationCredentialService(
    PbmDbContext db,
    IUserContext currentUser,
    IPasswordHasher<AppUser> passwordHasher) : IIntegrationCredentialService
{
    private const int SecretIterations = 210_000;
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(180);
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(730);

    public async Task<IReadOnlyList<IntegrationCredentialDto>> GetCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var utcNow = DateTime.UtcNow;
        return await db.Set<IntegrationCredential>().AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId)
            .OrderBy(x => x.User!.UserName)
            .ThenBy(x => x.Name)
            .Select(x => new IntegrationCredentialDto(
                x.Id,
                x.UserId,
                x.User!.UserName,
                x.Name,
                x.ClientId,
                x.ExpiresAtUtc,
                x.LastUsedAtUtc,
                x.RevokedAtUtc,
                x.RevocationReason,
                x.RevokedAtUtc == null && (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc.Value > utcNow)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IntegrationCredentialSecretDto> CreateServiceAccountAsync(
        CreateIntegrationServiceAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var userName = NormalizeUserName(request.UserName);
        var displayName = NormalizeDisplayName(request.DisplayName);
        var credentialName = NormalizeName(request.CredentialName);
        var expiresAtUtc = NormalizeExpiry(request.ExpiresAtUtc);
        await EnsureLicenseAllowsAnotherUserAsync(cancellationToken);

        if (await db.Users.AnyAsync(x => x.TenantId == currentUser.TenantId && x.UserName == userName, cancellationToken))
            throw new InvalidOperationException("A user with this username already exists.");

        var integrationRole = await db.Roles.SingleOrDefaultAsync(x =>
            x.TenantId == currentUser.TenantId && x.Code == "INTEGRATION",
            cancellationToken)
            ?? throw new InvalidOperationException("The standard INTEGRATION role is not provisioned for this tenant.");
        var companyAccess = await ResolveCompanyAccessAsync(request.CompanyAccess, cancellationToken);
        if (!companyAccess.Any(x => x.Input.CanWrite))
            throw new ArgumentException("An integration service account must have write access to at least one company.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new AppUser
        {
            TenantId = currentUser.TenantId,
            UserName = userName,
            DisplayName = displayName,
            PasswordHash = "pending",
            IsActive = true
        };
        var hiddenPassword = GenerateSecret() + GenerateSecret();
        user.PasswordHash = passwordHasher.HashPassword(user, hiddenPassword);
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = integrationRole.Id });
        foreach (var item in companyAccess)
        {
            user.CompanyAccess.Add(new UserCompanyAccess
            {
                UserId = user.Id,
                CompanyId = item.Company.Id,
                CanRead = item.Input.CanRead || item.Input.CanWrite,
                CanWrite = item.Input.CanWrite
            });
        }
        db.Users.Add(user);

        var (credential, secret) = BuildCredential(user.Id, credentialName, expiresAtUtc);
        db.Set<IntegrationCredential>().Add(credential);
        AddAudit(user.Id, "INTEGRATION_SERVICE_ACCOUNT_CREATE", new
        {
            user.UserName,
            user.DisplayName,
            Role = "INTEGRATION",
            Companies = companyAccess.Select(x => new { x.Company.Code, x.Input.CanRead, x.Input.CanWrite })
        }, "AppUser");
        AddAudit(credential.Id, "INTEGRATION_CREDENTIAL_CREATE", new
        {
            credential.UserId,
            credential.Name,
            credential.ClientId,
            credential.ExpiresAtUtc
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new IntegrationCredentialSecretDto(ToDto(credential, user.UserName, DateTime.UtcNow), secret);
    }

    public async Task<IntegrationCredentialSecretDto> CreateAsync(
        CreateIntegrationCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var name = NormalizeName(request.Name);
        var user = await LoadIntegrationUserAsync(request.UserId, cancellationToken);
        var expiresAtUtc = NormalizeExpiry(request.ExpiresAtUtc);
        var (credential, secret) = BuildCredential(user.Id, name, expiresAtUtc);

        db.Set<IntegrationCredential>().Add(credential);
        AddAudit(credential.Id, "INTEGRATION_CREDENTIAL_CREATE", new
        {
            credential.UserId,
            credential.Name,
            credential.ClientId,
            credential.ExpiresAtUtc
        });
        await db.SaveChangesAsync(cancellationToken);
        return new IntegrationCredentialSecretDto(ToDto(credential, user.UserName, DateTime.UtcNow), secret);
    }

    public async Task<IntegrationCredentialSecretDto> RotateAsync(
        Guid credentialId,
        RotateIntegrationCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var credential = await db.Set<IntegrationCredential>()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == credentialId && x.TenantId == currentUser.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Integration credential was not found.");
        if (credential.RevokedAtUtc.HasValue)
            throw new InvalidOperationException("A revoked integration credential cannot be rotated. Create a new credential instead.");
        if (credential.User is null || !credential.User.IsActive)
            throw new InvalidOperationException("The integration service account is inactive or missing.");

        var secret = GenerateSecret();
        var (salt, hash) = HashSecret(secret, SecretIterations);
        credential.SecretSalt = Convert.ToBase64String(salt);
        credential.SecretHash = Convert.ToBase64String(hash);
        credential.SecretIterations = SecretIterations;
        credential.ExpiresAtUtc = NormalizeExpiry(request.ExpiresAtUtc);
        credential.UpdatedAtUtc = DateTime.UtcNow;
        credential.User.TokenVersion++;
        credential.User.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(credential.Id, "INTEGRATION_CREDENTIAL_ROTATE", new
        {
            credential.ClientId,
            credential.ExpiresAtUtc,
            credential.User.TokenVersion,
            RotatedBy = currentUser.UserId
        });
        await db.SaveChangesAsync(cancellationToken);

        return new IntegrationCredentialSecretDto(
            ToDto(credential, credential.User.UserName, DateTime.UtcNow),
            secret);
    }

    public async Task RevokeAsync(
        Guid credentialId,
        RevokeIntegrationCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length is < 5 or > 500)
            throw new ArgumentException("Revocation reason must contain 5-500 characters.");

        var credential = await db.Set<IntegrationCredential>()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == credentialId && x.TenantId == currentUser.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Integration credential was not found.");
        if (credential.RevokedAtUtc.HasValue) return;

        credential.RevokedAtUtc = DateTime.UtcNow;
        credential.RevokedByUserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId;
        credential.RevocationReason = reason;
        credential.UpdatedAtUtc = DateTime.UtcNow;
        if (credential.User is not null)
        {
            credential.User.TokenVersion++;
            credential.User.UpdatedAtUtc = DateTime.UtcNow;
        }
        AddAudit(credential.Id, "INTEGRATION_CREDENTIAL_REVOKE", new
        {
            credential.ClientId,
            credential.RevokedAtUtc,
            credential.RevokedByUserId,
            credential.RevocationReason,
            TokenVersion = credential.User?.TokenVersion
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IntegrationIdentityDto?> ValidateAsync(
        ClientCredentialsRequest request,
        CancellationToken cancellationToken = default)
    {
        var clientId = (request.ClientId ?? string.Empty).Trim().ToLowerInvariant();
        var clientSecret = request.ClientSecret ?? string.Empty;
        if (clientId.Length is < 8 or > 80 || clientSecret.Length is < 32 or > 256) return null;

        var credential = await db.Set<IntegrationCredential>()
            .Include(x => x.User).ThenInclude(x => x!.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.User).ThenInclude(x => x!.CompanyAccess)
            .SingleOrDefaultAsync(x => x.ClientId == clientId, cancellationToken);
        if (credential?.User is null) return null;

        var utcNow = DateTime.UtcNow;
        if (!credential.IsActive(utcNow) || !credential.User.IsActive) return null;
        if (!VerifySecret(clientSecret, credential)) return null;

        var roleCodes = credential.User.UserRoles
            .Where(x => x.Role is not null)
            .Select(x => x.Role!.Code)
            .ToArray();
        if (roleCodes.Length != 1 || !string.Equals(roleCodes[0], "INTEGRATION", StringComparison.OrdinalIgnoreCase))
            return null;

        var readableCompanies = credential.User.CompanyAccess
            .Where(x => x.CanRead || x.CanWrite)
            .Select(x => x.CompanyId)
            .Distinct()
            .ToArray();
        var writableCompanies = credential.User.CompanyAccess
            .Where(x => x.CanWrite)
            .Select(x => x.CompanyId)
            .Distinct()
            .ToArray();
        if (writableCompanies.Length == 0) return null;

        credential.LastUsedAtUtc = utcNow;
        credential.UpdatedAtUtc = utcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new IntegrationIdentityDto(
            credential.Id,
            credential.User.Id,
            credential.User.TenantId,
            credential.User.UserName,
            credential.User.DisplayName,
            credential.User.TokenVersion,
            ["INTEGRATION"],
            readableCompanies,
            writableCompanies);
    }

    private (IntegrationCredential Credential, string Secret) BuildCredential(Guid userId, string name, DateTime expiresAtUtc)
    {
        var secret = GenerateSecret();
        var (salt, hash) = HashSecret(secret, SecretIterations);
        var credential = new IntegrationCredential
        {
            TenantId = currentUser.TenantId,
            UserId = userId,
            Name = name,
            ClientId = $"pbm_{Guid.NewGuid():N}",
            SecretSalt = Convert.ToBase64String(salt),
            SecretHash = Convert.ToBase64String(hash),
            SecretIterations = SecretIterations,
            ExpiresAtUtc = expiresAtUtc
        };
        return (credential, secret);
    }

    private async Task<AppUser> LoadIntegrationUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.CompanyAccess)
            .SingleOrDefaultAsync(x => x.Id == userId && x.TenantId == currentUser.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("User was not found.");
        if (!user.IsActive) throw new InvalidOperationException("Integration credentials can only be issued to active accounts.");
        var roleCodes = user.UserRoles.Where(x => x.Role is not null).Select(x => x.Role!.Code).ToArray();
        if (roleCodes.Length != 1 || !string.Equals(roleCodes[0], "INTEGRATION", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Integration credentials can only be issued to dedicated accounts whose only role is INTEGRATION.");
        if (!user.CompanyAccess.Any(x => x.CanWrite))
            throw new InvalidOperationException("The selected integration account must have write access to at least one company.");
        return user;
    }

    private async Task<IReadOnlyList<(Company Company, UserCompanyAccessInput Input)>> ResolveCompanyAccessAsync(
        IReadOnlyList<UserCompanyAccessInput> inputs,
        CancellationToken cancellationToken)
    {
        var normalized = (inputs ?? [])
            .GroupBy(x => x.CompanyId)
            .Select(group => group.Last())
            .ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("At least one company access entry is required for an integration service account.");

        var ids = normalized.Select(x => x.CompanyId).ToArray();
        var companies = await db.Companies
            .Where(x => x.TenantId == currentUser.TenantId && x.IsActive && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (companies.Count != ids.Length)
            throw new ArgumentException("One or more selected companies are invalid or inactive.");

        return normalized.Select(input =>
            (companies[input.CompanyId], new UserCompanyAccessInput(input.CompanyId, input.CanRead || input.CanWrite, input.CanWrite)))
            .ToList();
    }

    private async Task EnsureLicenseAllowsAnotherUserAsync(CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var license = await db.LicenseSubscriptions.AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No license is configured for this tenant.");
        if (!license.IsActive || license.StartsAtUtc > utcNow || license.ExpiresAtUtc < utcNow)
            throw new InvalidOperationException("The tenant license is not active.");
        var activeUsers = await db.Users.CountAsync(x => x.TenantId == currentUser.TenantId && x.IsActive, cancellationToken);
        if (activeUsers >= license.MaxUsers)
            throw new InvalidOperationException($"The license allows a maximum of {license.MaxUsers} active users, including integration service accounts.");
    }

    private void EnsureAdmin()
    {
        if (!currentUser.IsInRole("SUPERADMIN") && !currentUser.IsInRole("ADMIN"))
            throw new UnauthorizedAccessException("Administrator role is required to manage integration credentials.");
    }

    private static DateTime NormalizeExpiry(DateTime? requested)
    {
        var utcNow = DateTime.UtcNow;
        var expiry = requested.HasValue
            ? requested.Value.Kind switch
            {
                DateTimeKind.Utc => requested.Value,
                DateTimeKind.Local => requested.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(requested.Value, DateTimeKind.Utc)
            }
            : utcNow.Add(DefaultLifetime);
        if (expiry <= utcNow.AddMinutes(5))
            throw new ArgumentException("Integration credential expiry must be at least five minutes in the future.");
        if (expiry > utcNow.Add(MaximumLifetime))
            throw new ArgumentException("Integration credential expiry cannot be more than two years in the future.");
        return expiry;
    }

    private static string NormalizeUserName(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Length is < 3 or > 80 || text.Any(char.IsWhiteSpace))
            throw new ArgumentException("Service-account username must contain 3-80 characters without spaces.");
        return text;
    }

    private static string NormalizeDisplayName(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length is < 2 or > 160)
            throw new ArgumentException("Service-account display name must contain 2-160 characters.");
        return text;
    }

    private static string NormalizeName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 160)
            throw new ArgumentException("Integration credential name must contain 2-160 characters.");
        return name;
    }

    private static string GenerateSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static (byte[] Salt, byte[] Hash) HashSecret(string secret, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, 32);
        return (salt, hash);
    }

    private static bool VerifySecret(string secret, IntegrationCredential credential)
    {
        try
        {
            var salt = Convert.FromBase64String(credential.SecretSalt);
            var expected = Convert.FromBase64String(credential.SecretHash);
            if (credential.SecretIterations < 100_000 || expected.Length != 32 || salt.Length < 16) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, credential.SecretIterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void AddAudit(Guid entityId, string action, object newValue, string entityType = "IntegrationCredential") =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = currentUser.TenantId,
            UserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Action = action,
            NewValueJson = JsonSerializer.Serialize(newValue)
        });

    private static IntegrationCredentialDto ToDto(IntegrationCredential credential, string userName, DateTime utcNow) => new(
        credential.Id,
        credential.UserId,
        userName,
        credential.Name,
        credential.ClientId,
        credential.ExpiresAtUtc,
        credential.LastUsedAtUtc,
        credential.RevokedAtUtc,
        credential.RevocationReason,
        credential.IsActive(utcNow));
}
