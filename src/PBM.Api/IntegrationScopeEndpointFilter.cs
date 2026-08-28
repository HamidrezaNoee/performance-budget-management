using System.Security.Claims;

namespace PBM.Api;

public sealed class IntegrationScopeEndpointFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
    {
        var context = invocationContext.HttpContext;
        var authMethod = context.User.FindFirstValue("auth_method");
        if (!string.Equals(authMethod, "client_credentials", StringComparison.OrdinalIgnoreCase))
            return next(invocationContext);

        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/v1/actual-ledger", StringComparison.OrdinalIgnoreCase))
            return next(invocationContext);

        return ValueTask.FromResult<object?>(Results.Forbid());
    }
}
