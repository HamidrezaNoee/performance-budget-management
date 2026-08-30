using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;

namespace PBM.Api;

public static class CaptchaEndpoints
{
    private const string CaptchaIdHeader = "X-PBM-Captcha-Id";
    private const string CaptchaAnswerHeader = "X-PBM-Captcha-Answer";
    private const int MaxOutstandingChallenges = 5000;
    private static readonly ConcurrentDictionary<string, CaptchaEntry> Challenges = new(StringComparer.Ordinal);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public static IEndpointRouteBuilder MapPbmCaptchaEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/auth/captcha", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            return Results.Ok(CreateChallenge());
        }).AllowAnonymous();

        return endpoints;
    }

    internal static bool ValidateAndConsume(HttpContext context)
    {
        var captchaId = context.Request.Headers[CaptchaIdHeader].FirstOrDefault();
        var answer = context.Request.Headers[CaptchaAnswerHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(answer)) return false;

        // A challenge is removed before validation so every captcha is single-use,
        // including incorrect attempts. This prevents replay and answer probing.
        if (!Challenges.TryRemove(captchaId, out var challenge)) return false;
        if (challenge.ExpiresAtUtc < DateTime.UtcNow) return false;
        if (!int.TryParse(answer.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var providedAnswer)) return false;
        return providedAnswer == challenge.Answer;
    }

    private static CaptchaResponse CreateChallenge()
    {
        var now = DateTime.UtcNow;
        PruneChallenges(now);

        var left = RandomNumberGenerator.GetInt32(2, 10);
        var right = RandomNumberGenerator.GetInt32(2, 10);
        var useAddition = RandomNumberGenerator.GetInt32(0, 2) == 0;
        int answer;
        string challenge;

        if (useAddition)
        {
            answer = left + right;
            challenge = $"{left} + {right} = ?";
        }
        else
        {
            if (right > left) (left, right) = (right, left);
            answer = left - right;
            challenge = $"{left} - {right} = ?";
        }

        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();
        Challenges[id] = new CaptchaEntry(answer, now.Add(Lifetime));
        return new CaptchaResponse(id, challenge, (int)Lifetime.TotalSeconds);
    }

    private static void PruneChallenges(DateTime now)
    {
        foreach (var pair in Challenges)
        {
            if (pair.Value.ExpiresAtUtc < now) Challenges.TryRemove(pair.Key, out _);
        }

        if (Challenges.Count < MaxOutstandingChallenges) return;

        // The endpoint is intentionally stateless from the browser's perspective,
        // but bound the in-memory challenge store so abusive refresh traffic cannot
        // make it grow without limit. Remove the entries closest to expiration first.
        var removeCount = Math.Max(1, MaxOutstandingChallenges / 10);
        foreach (var pair in Challenges.OrderBy(x => x.Value.ExpiresAtUtc).Take(removeCount))
            Challenges.TryRemove(pair.Key, out _);
    }

    private sealed record CaptchaEntry(int Answer, DateTime ExpiresAtUtc);
    public sealed record CaptchaResponse(string CaptchaId, string Challenge, int ExpiresInSeconds);
}

public sealed class CaptchaLoginEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!CaptchaEndpoints.ValidateAndConsume(context.HttpContext))
        {
            return Results.BadRequest(new
            {
                title = "Invalid captcha",
                detail = "کد امنیتی نادرست یا منقضی شده است. لطفاً کد جدید را وارد کنید."
            });
        }

        return await next(context);
    }
}
