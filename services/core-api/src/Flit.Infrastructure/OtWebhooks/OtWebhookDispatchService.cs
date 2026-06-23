using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.OtWebhooks;

namespace Flit.Infrastructure.OtWebhooks;

/// <summary>
/// Despacha webhooks OT con firma HMAC-SHA256 y bitácora outbound (HU #10216 AC2).
/// </summary>
internal sealed class OtWebhookDispatchService : IOtWebhookDispatchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOtWebhookSubscriptionRepository _subscriptionRepository;
    private readonly IOtApiCallLogRepository _apiCallLogRepository;
    private readonly IHttpClientFactory _httpClientFactory;

    public OtWebhookDispatchService(
        IOtWebhookSubscriptionRepository subscriptionRepository,
        IOtApiCallLogRepository apiCallLogRepository,
        IHttpClientFactory httpClientFactory)
    {
        _subscriptionRepository = subscriptionRepository
            ?? throw new ArgumentNullException(nameof(subscriptionRepository));
        _apiCallLogRepository = apiCallLogRepository
            ?? throw new ArgumentNullException(nameof(apiCallLogRepository));
        _httpClientFactory = httpClientFactory
            ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task DispatchAsync(
        Guid tenantId,
        string eventType,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await _subscriptionRepository
            .ListActiveByEventTypeAsync(tenantId, eventType, cancellationToken)
            .ConfigureAwait(false);

        if (subscriptions.Count == 0)
        {
            return;
        }

        var body = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var correlationId = Guid.NewGuid();
        var client = _httpClientFactory.CreateClient(nameof(OtWebhookDispatchService));

        foreach (var subscription in subscriptions)
        {
            await DispatchToSubscriptionAsync(
                tenantId,
                subscription,
                body,
                payloadHash,
                correlationId,
                client,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchToSubscriptionAsync(
        Guid tenantId,
        OtWebhookSubscription subscription,
        string body,
        string payloadHash,
        Guid correlationId,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var signingKey = OtWebhookSecretHasher.SigningKeyFromStoredHash(subscription.SecretHash);
        var signature = ComputeHmacSha256Hex(body, signingKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.TargetUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        var stopwatch = Stopwatch.StartNew();
        short? responseCode = null;

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            responseCode = (short)(int)response.StatusCode;
        }
        catch
        {
            responseCode = 0;
        }
        finally
        {
            stopwatch.Stop();
            await _apiCallLogRepository.AppendAsync(
                tenantId,
                direction: "outbound",
                endpoint: subscription.TargetUrl,
                httpMethod: "POST",
                payloadHash: payloadHash,
                responseCode: responseCode,
                durationMs: (int)stopwatch.ElapsedMilliseconds,
                correlationId: correlationId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ComputeHmacSha256Hex(string payload, byte[] key)
    {
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
