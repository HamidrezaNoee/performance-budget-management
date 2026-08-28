using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using PBM.Application;

namespace PBM.Api;

public sealed partial class IdempotencyEndpointFilter(
    IIdempotencyService idempotency,
    IConfiguration configuration,
    ILogger<IdempotencyEndpointFilter> logger) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string StatusHeaderName = "Idempotency-Status";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
    {
        var context = invocationContext.HttpContext;
        if (!IsWriteMethod(context.Request.Method)
            || context.Request.HasFormContentType
            || !context.Request.Headers.TryGetValue(HeaderName, out var values))
            return await next(invocationContext);

        var key = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(key)) return await next(invocationContext);
        if (key.Length is < 8 or > 100 || !IdempotencyKeyRegex().IsMatch(key))
            return Results.BadRequest(new
            {
                code = "INVALID_IDEMPOTENCY_KEY",
                detail = "Idempotency-Key must contain 8-100 letters, numbers, dot, underscore, colon or dash characters."
            });

        var retentionHours = configuration.GetValue<int?>("Idempotency:RetentionHours") ?? 168;
        if (retentionHours is < 1 or > 720)
            throw new InvalidOperationException("Idempotency:RetentionHours must be between 1 and 720.");

        var scope = $"{context.Request.Method.ToUpperInvariant()} {context.Request.Path.Value}";
        var requestHash = ComputeRequestHash(context, invocationContext.Arguments);
        var correlationId = context.GetCorrelationId();
        var begin = await idempotency.BeginAsync(
            key,
            scope,
            requestHash,
            correlationId,
            TimeSpan.FromHours(retentionHours),
            context.RequestAborted);

        context.Response.Headers[StatusHeaderName] = begin.Disposition.ToString();
        switch (begin.Disposition)
        {
            case IdempotencyBeginDisposition.PayloadConflict:
                return Conflict("IDEMPOTENCY_PAYLOAD_CONFLICT",
                    "The same Idempotency-Key was already used for this endpoint with a different request payload.", begin);
            case IdempotencyBeginDisposition.AlreadyCompleted:
                return Conflict("IDEMPOTENCY_ALREADY_COMPLETED",
                    "This idempotent operation has already completed and will not be executed again.", begin);
            case IdempotencyBeginDisposition.AlreadyProcessing:
                return Conflict("IDEMPOTENCY_IN_PROGRESS",
                    "An operation with this Idempotency-Key is already being processed.", begin);
            case IdempotencyBeginDisposition.Uncertain:
                return Conflict("IDEMPOTENCY_REQUIRES_RECONCILIATION",
                    "A previous attempt ended in an uncertain state. Reconcile the business result before using a new Idempotency-Key.", begin);
            case IdempotencyBeginDisposition.Acquired:
                break;
            default:
                throw new InvalidOperationException("Unsupported idempotency disposition.");
        }

        if (!begin.RecordId.HasValue)
            throw new InvalidOperationException("Acquired idempotency operation has no record id.");

        try
        {
            var result = await next(invocationContext);
            await idempotency.CompleteAsync(begin.RecordId.Value, context.RequestAborted);
            context.Response.Headers[StatusHeaderName] = "Completed";
            return result;
        }
        catch (Exception ex)
        {
            try
            {
                await idempotency.MarkUncertainAsync(begin.RecordId.Value, ex, CancellationToken.None);
            }
            catch (Exception persistenceException)
            {
                logger.LogError(persistenceException,
                    "Failed to mark idempotency record {RecordId} uncertain after endpoint failure",
                    begin.RecordId.Value);
            }
            throw;
        }
    }

    private static IResult Conflict(string code, string detail, IdempotencyBeginResult result) =>
        Results.Json(new
        {
            code,
            detail,
            originalCorrelationId = result.OriginalCorrelationId,
            expiresAtUtc = result.ExpiresAtUtc
        }, statusCode: StatusCodes.Status409Conflict);

    internal static string ComputeRequestHash(HttpContext context, IList<object?> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(context.Request.Method.ToUpperInvariant()).Append('|')
            .Append(context.Request.Path.Value).Append('|')
            .Append(context.Request.QueryString.Value).Append('|');

        foreach (var argument in arguments)
        {
            if (!ShouldFingerprint(argument)) continue;
            var type = argument!.GetType();
            builder.Append(type.FullName).Append('=');
            builder.Append(JsonSerializer.Serialize(argument, type));
            builder.Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static bool ShouldFingerprint(object? argument)
    {
        if (argument is null) return false;
        if (argument is CancellationToken or HttpContext or HttpRequest or HttpResponse or IServiceProvider or Stream or IFormFile)
            return false;

        var type = argument.GetType();
        if (type.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("Microsoft.Extensions", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("PBM.Infrastructure", StringComparison.Ordinal) == true)
            return false;

        if (type.IsInterface && type.Namespace?.StartsWith("PBM.Application", StringComparison.Ordinal) == true)
            return false;

        return true;
    }

    private static bool IsWriteMethod(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{7,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyRegex();
}
