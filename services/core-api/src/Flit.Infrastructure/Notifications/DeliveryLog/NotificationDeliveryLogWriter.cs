using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;

namespace Flit.Infrastructure.Notifications.DeliveryLog;

/// <summary>
/// Inserta una fila en <c>admin.notification_delivery_logs</c> (HU #11363). Append-only: nunca
/// actualiza ni borra. Usa <see cref="TenantRlsScope"/> — mismo patrón que
/// <c>CompanyPersonalizedDocumentRepository</c> — para fijar <c>app.current_tenant_id</c> en su
/// propia transacción; el RLS de la tabla es decorativo (sin <c>FORCE ROW LEVEL SECURITY</c>), así
/// que esto es defensa en profundidad, no la garantía de aislamiento (esa es AC3 en la consulta,
/// con <c>WHERE tenant_id</c> explícito).
/// </summary>
internal sealed class NotificationDeliveryLogWriter(FlitDbContext db) : INotificationDeliveryLogWriter
{
    public Task WriteAsync(NotificationDeliveryLogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return TenantRlsScope.ExecuteAsync(
            db,
            entry.TenantId,
            async () =>
            {
                db.NotificationDeliveryLogs.Add(new NotificationDeliveryLogEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = entry.TenantId,
                    TemplateKey = entry.TemplateKey,
                    Channel = entry.Channel,
                    Recipient = entry.Recipient,
                    Result = entry.Success ? "enviado" : "fallido",
                    FailureReason = entry.Success ? null : entry.FailureReason,
                    DurationMs = entry.DurationMs,
                    RecipientDiverted = entry.RecipientDiverted,
                    OccurredAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                });

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }
}
