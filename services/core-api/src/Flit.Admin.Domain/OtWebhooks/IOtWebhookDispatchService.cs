namespace Flit.Admin.Domain.OtWebhooks;

/// <summary>
/// Despacha webhooks OT activos y registra cada intento en la bitácora (HU #10216 AC2).
/// </summary>
public interface IOtWebhookDispatchService
{
    Task DispatchAsync(
        Guid tenantId,
        string eventType,
        object payload,
        CancellationToken cancellationToken = default);
}
