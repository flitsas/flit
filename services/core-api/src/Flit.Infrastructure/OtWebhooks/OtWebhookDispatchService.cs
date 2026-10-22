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
/// Webhooks OT con firma HMAC-SHA256 y bitácora outbound (HU #10216 AC2). Desde el corte (HU #13359) core-api no hace
/// el POST: firma el cuerpo y lo deja como trabajo <c>notificaciones.webhooks.salientes</c> (HU #13356), que entrega
/// Notificaciones con su filtro de destinos internos, reintentos y mensajes muertos. La llave de firma no sale de
/// core-api: viaja la firma, no la llave. La bitácora outbound registra el envío encolado (sin código de respuesta).
/// </summary>
internal sealed class OtWebhookDispatchService(
    IOtWebhookSubscriptionRepository subscriptionRepository,
    IOtApiCallLogRepository apiCallLogRepository,
    IServiceScopeFactory scopes) : IOtWebhookDispatchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task DispatchAsync(
        Guid tenantId,
        string eventType,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository
            .ListActiveByEventTypeAsync(tenantId, eventType, cancellationToken)
            .ConfigureAwait(false);

        if (subscriptions.Count == 0)
        {
            return;
        }

        var body = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var correlationId = Guid.NewGuid();

        foreach (var subscription in subscriptions)
        {
            var signingKey = OtWebhookSecretHasher.SigningKeyFromStoredHash(subscription.SecretHash);
            var signature = ComputeHmacSha256Hex(body, signingKey);
            await EncolarAsync(tenantId, subscription, body, signature, correlationId, cancellationToken).ConfigureAwait(false);
            await apiCallLogRepository.AppendAsync(
                tenantId, direction: "outbound", endpoint: subscription.TargetUrl, httpMethod: "POST", payloadHash: payloadHash,
                responseCode: null, durationMs: 0, correlationId: correlationId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EncolarAsync(
        Guid tenantId, OtWebhookSubscription subscription, string body, string signature, Guid correlationId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
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
