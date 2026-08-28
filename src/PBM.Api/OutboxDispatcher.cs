using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PBM.Infrastructure;

namespace PBM.Api;

public interface IOutboxTransport
{
    bool CanHandle(string messageType, string destination);
    Task DeliverAsync(OutboxDispatchItem item, CancellationToken cancellationToken);
}

public sealed class NotificationWebhookTransport(
    HttpClient httpClient,
    IConfiguration configuration) : IOutboxTransport
{
    public bool CanHandle(string messageType, string destination) =>
        string.Equals(messageType, "notification.webhook.v1", StringComparison.OrdinalIgnoreCase)
        && string.Equals(destination, "notification-webhook", StringComparison.OrdinalIgnoreCase);

    public async Task DeliverAsync(OutboxDispatchItem item, CancellationToken cancellationToken)
    {
        var urlText = configuration["OutboundNotifications:Webhook:Url"]?.Trim();
        if (string.IsNullOrWhiteSpace(urlText))
            throw new InvalidOperationException("Outbound notification webhook URL is not configured.");
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Outbound notification webhook URL must be an absolute HTTP(S) URL.");
        var allowHttp = configuration.GetValue<bool>("OutboundNotifications:Webhook:AllowHttp");
        if (uri.Scheme == Uri.UriSchemeHttp && !allowHttp)
            throw new InvalidOperationException("Plain HTTP notification webhooks are disabled. Use HTTPS or explicitly enable AllowHttp for a trusted local environment.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Webhook URL user-info credentials are not allowed. Configure authentication headers through deployment secrets instead.");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(item.PayloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-PBM-Outbox-Id", item.Id.ToString());
        request.Headers.TryAddWithoutValidation("X-PBM-Delivery-Attempt", item.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(item.CorrelationId))
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", item.CorrelationId);

        var bearer = configuration["OutboundNotifications:Webhook:BearerToken"]?.Trim();
        if (!string.IsNullOrWhiteSpace(bearer))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var body = await ReadErrorBodyAsync(response, cancellationToken);
        throw new HttpRequestException(
            $"Notification webhook returned HTTP {(int)response.StatusCode} ({response.StatusCode}).{(string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {body}")}",
            null,
            response.StatusCode);
    }

    private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null) return string.Empty;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 1000) body = body[..1000];
        return body.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}

public sealed class OutboxDispatcherBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OutboxDispatcherBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = Math.Clamp(configuration.GetValue<int?>("Outbox:PollSeconds") ?? 5, 1, 300);
        logger.LogInformation("PBM outbox dispatcher started with a {PollSeconds}-second polling interval", pollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DispatchBatchAsync(stoppingToken);
                if (processed == 0)
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PBM outbox dispatcher iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<OutboxQueueService>();
        var transports = scope.ServiceProvider.GetServices<IOutboxTransport>().ToArray();
        var batch = await queue.ClaimBatchAsync(cancellationToken);

        foreach (var item in batch)
        {
            try
            {
                var transport = transports.FirstOrDefault(x => x.CanHandle(item.MessageType, item.Destination))
                    ?? throw new NotSupportedException($"No outbox transport is registered for {item.MessageType}/{item.Destination}.");
                await transport.DeliverAsync(item, cancellationToken);
                await queue.CompleteAsync(item, cancellationToken);
                logger.LogInformation(
                    "Outbox message {OutboxId} delivered to {Destination} on attempt {Attempt}",
                    item.Id,
                    item.Destination,
                    item.Attempts);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await queue.FailAsync(item, ex, cancellationToken);
                logger.LogWarning(
                    ex,
                    "Outbox message {OutboxId} delivery failed on attempt {Attempt}",
                    item.Id,
                    item.Attempts);
            }
        }

        return batch.Count;
    }
}
