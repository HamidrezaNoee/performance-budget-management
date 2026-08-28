namespace PBM.Application;

public sealed record IntegrationCredentialDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string Name,
    string ClientId,
    DateTime? ExpiresAtUtc,
    DateTime? LastUsedAtUtc,
    DateTime? RevokedAtUtc,
    string? RevocationReason,
    bool IsActive);

public sealed record CreateIntegrationServiceAccountRequest(
    string UserName,
    string DisplayName,
    string CredentialName,
    IReadOnlyList<UserCompanyAccessInput> CompanyAccess,
    DateTime? ExpiresAtUtc);

public sealed record CreateIntegrationCredentialRequest(
    Guid UserId,
    string Name,
    DateTime? ExpiresAtUtc);

public sealed record RotateIntegrationCredentialRequest(DateTime? ExpiresAtUtc);
public sealed record RevokeIntegrationCredentialRequest(string Reason);

public sealed record IntegrationCredentialSecretDto(
    IntegrationCredentialDto Credential,
    string ClientSecret);

public sealed record ClientCredentialsRequest(string ClientId, string ClientSecret);

public sealed record IntegrationIdentityDto(
    Guid CredentialId,
    Guid UserId,
    Guid TenantId,
    string UserName,
    string DisplayName,
    int TokenVersion,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> CompanyIds,
    IReadOnlyList<Guid> WritableCompanyIds);

public sealed record IntegrationTokenResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string TokenType,
    string ClientId);

public interface IIntegrationCredentialService
{
    Task<IReadOnlyList<IntegrationCredentialDto>> GetCredentialsAsync(CancellationToken cancellationToken = default);
    Task<IntegrationCredentialSecretDto> CreateServiceAccountAsync(CreateIntegrationServiceAccountRequest request, CancellationToken cancellationToken = default);
    Task<IntegrationCredentialSecretDto> CreateAsync(CreateIntegrationCredentialRequest request, CancellationToken cancellationToken = default);
    Task<IntegrationCredentialSecretDto> RotateAsync(Guid credentialId, RotateIntegrationCredentialRequest request, CancellationToken cancellationToken = default);
    Task RevokeAsync(Guid credentialId, RevokeIntegrationCredentialRequest request, CancellationToken cancellationToken = default);
    Task<IntegrationIdentityDto?> ValidateAsync(ClientCredentialsRequest request, CancellationToken cancellationToken = default);
}
