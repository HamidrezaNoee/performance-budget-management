using System.Diagnostics;

namespace PBM.Api;

public sealed class CorrelationIdEndpointFilter(ILogger<CorrelationIdEndpointFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
    {
        var context = invocationContext.HttpContext;
        var correlationId = CorrelationIdMiddleware.ResolveCorrelationId(context);
        context.Items[CorrelationIdMiddleware.ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdMiddleware.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString(),
            ["RequestMethod"] = context.Request.Method,
            ["RequestPath"] = context.Request.Path.Value
        });

        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await next(invocationContext);
            logger.LogInformation(
                "API {Method} {Path} endpoint completed in {ElapsedMs:F1} ms",
                context.Request.Method,
                context.Request.Path.Value,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch
        {
            logger.LogWarning(
                "API {Method} {Path} endpoint failed after {ElapsedMs:F1} ms; exception handler will assign the final response status",
                context.Request.Method,
                context.Request.Path.Value,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
