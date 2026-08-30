using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PBM.Application;

namespace PBM.Api;

public static class IntegrationCredentialEndpoints
{
    public static RouteGroupBuilder MapIntegrationCredentialEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/auth/client-token", async (
            ClientCredentialsRequest request,
            IIntegrationCredentialService credentials,
            IConfiguration configuration,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var identity = await credentials.ValidateAsync(request, ct);
            if (identity is null) return Results.Unauthorized();

            var keyText = configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(keyText) || Encoding.UTF8.GetByteCount(keyText) < 32)
                throw new InvalidOperationException("Jwt:Key is not configured for integration token issuance.");
            var tokenMinutes = configuration.GetValue<int?>("IntegrationAuth:TokenMinutes") ?? 30;
            if (tokenMinutes is < 5 or > 120)
                throw new InvalidOperationException("IntegrationAuth:TokenMinutes must be between 5 and 120.");

            var expiresAtUtc = DateTime.UtcNow.AddMinutes(tokenMinutes);
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, identity.UserId.ToString()),
                new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
                new("tenant_id", identity.TenantId.ToString()),
                new("token_version", identity.TokenVersion.ToString(CultureInfo.InvariantCulture)),
                new(ClaimTypes.Name, identity.DisplayName),
                new("username", identity.UserName),
                new("auth_method", "client_credentials"),
                new("integration_credential_id", identity.CredentialId.ToString()),
                new("client_id", request.ClientId.Trim().ToLowerInvariant())
            };
            claims.AddRange(identity.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            claims.AddRange(identity.CompanyIds.Select(companyId => new Claim("company_id", companyId.ToString())));
            claims.AddRange(identity.WritableCompanyIds.Select(companyId => new Claim("company_write_id", companyId.ToString())));

            var token = new JwtSecurityToken(
                configuration["Jwt:Issuer"],
                configuration["Jwt:Audience"],
                claims,
                notBefore: DateTime.UtcNow,
                expires: expiresAtUtc,
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText)),
                    SecurityAlgorithms.HmacSha256));

            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            return Results.Ok(new IntegrationTokenResponse(
                new JwtSecurityTokenHandler().WriteToken(token),
                expiresAtUtc,
                "Bearer",
                request.ClientId.Trim().ToLowerInvariant()));
        }).AllowAnonymous().RequireRateLimiting("login");

        var admin = api.MapGroup("/security/integration-credentials");
        admin.MapGet("/", (IIntegrationCredentialService service, CancellationToken ct) =>
            service.GetCredentialsAsync(ct));

        admin.MapPost("/service-accounts", async (
            CreateIntegrationServiceAccountRequest request,
            IIntegrationCredentialService service,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await service.CreateServiceAccountAsync(request, ct);
            NoStore(httpContext.Response);
            return Results.Ok(result);
        });

        admin.MapPost("/", async (
            CreateIntegrationCredentialRequest request,
            IIntegrationCredentialService service,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            NoStore(httpContext.Response);
            return Results.Ok(result);
        });

        admin.MapPost("/{credentialId:guid}/rotate", async (
            Guid credentialId,
            RotateIntegrationCredentialRequest request,
            IIntegrationCredentialService service,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await service.RotateAsync(credentialId, request, ct);
            NoStore(httpContext.Response);
            return Results.Ok(result);
        });

        admin.MapPost("/{credentialId:guid}/revoke", async (
            Guid credentialId,
            RevokeIntegrationCredentialRequest request,
            IIntegrationCredentialService service,
            CancellationToken ct) =>
        {
            await service.RevokeAsync(credentialId, request, ct);
            return Results.NoContent();
        });

        return api;
    }

    private static void NoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }
}
