using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.OtWebhooks;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Notificaciones.Webhooks;
using Flit.Platform.Sdk.Messaging;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IServiceScopeFactory? _scopes;
    private readonly bool _porNotificaciones;

    public OtWebhookDispatchService(
        IOtWebhookSubscriptionRepository subscriptionRepository,
        IOtApiCallLogRepository apiCallLogRepository,
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory? scopes = null,
        OtWebhooksPorNotificaciones? porNotificaciones = null)
    {
        _scopes = scopes;
        _porNotificaciones = porNotificaciones?.Habilitado == true;
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

        if (_porNotificaciones && _scopes is not null)
        {
            // HU #13356: el webhook sale ya armado y firmado por Notificaciones (filtro de destinos internos, reintentos,
            // mensajes muertos). La llave de firma no sale de core-api: viaja la firma, no la llave.
            await EncolarAsync(tenantId, subscription, body, signature, correlationId, cancellationToken).ConfigureAwait(false);
            await _apiCallLogRepository.AppendAsync(
                tenantId, direction: "outbound", endpoint: subscription.TargetUrl, httpMethod: "POST", payloadHash: payloadHash,
                responseCode: null, durationMs: 0, correlationId: correlationId, cancellationToken).ConfigureAwait(false);
            return;
        }

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

    private async Task EncolarAsync(
        Guid tenantId, OtWebhookSubscription subscription, string body, string signature, Guid correlationId, CancellationToken cancellationToken)
    {
        await using var scope = _scopes!.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPlatformOutbox>().EnqueueJob(
            TrabajoWebhook.Tipo,
            1,
            tenantId,
            new TrabajoWebhook(
                subscription.TargetUrl,
                body,
                new Dictionary<string, string>
                {
                    ["X-Webhook-Signature"] = $"sha256={signature}",
                    ["X-Correlation-Id"] = correlationId.ToString(),
                },
                Origen: "ot",
                Evento: subscription.EventType));
        await scope.ServiceProvider.GetRequiredService<FlitDbContext>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ComputeHmacSha256Hex(string payload, byte[] key)
    {
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>HU #13356 — <c>Notificaciones:Remoto:Webhooks</c>: los webhooks del OT salen por Notificaciones.</summary>
public sealed record OtWebhooksPorNotificaciones(bool Habilitado)
{
    public const string FlagKey = "Notificaciones:Remoto:Webhooks";
}
