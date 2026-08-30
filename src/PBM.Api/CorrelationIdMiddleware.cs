using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PBM.Api;

public sealed partial class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "PBM.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Items[ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString(),
            ["RequestMethod"] = context.Request.Method,
            ["RequestPath"] = context.Request.Path.Value
        }))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                logger.LogInformation(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:F1} ms",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Response.StatusCode,
                    elapsedMs);
            }
        }
    }

    public static string ResolveCorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied)
            && supplied.Length <= 100
            && CorrelationIdRegex().IsMatch(supplied))
            return supplied;

        var activityTraceId = Activity.Current?.TraceId.ToString();
        return !string.IsNullOrWhiteSpace(activityTraceId)
            ? activityTraceId
            : Guid.NewGuid().ToString("N");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdRegex();
}

public static class CorrelationIdHttpContextExtensions
{
    public static string GetCorrelationId(this HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) && value is string text
            ? text
            : context.TraceIdentifier;
}
