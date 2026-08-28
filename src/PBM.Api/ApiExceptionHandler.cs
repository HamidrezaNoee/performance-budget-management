using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PBM.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Access denied"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Operation cannot be completed"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error")
        };

        var correlationId = httpContext.GetCorrelationId();
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
        if (status >= 500) logger.LogError(exception, "Unhandled PBM API exception");
        else logger.LogWarning(exception, "PBM API request failed with status {StatusCode}", status);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status >= 500 ? "An unexpected error occurred." : exception.Message,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["correlationId"] = correlationId;

        httpContext.Response.StatusCode = status;
        httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName] = correlationId;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
