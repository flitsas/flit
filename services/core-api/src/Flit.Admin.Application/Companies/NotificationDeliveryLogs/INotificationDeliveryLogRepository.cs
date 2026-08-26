namespace Flit.Admin.Application.Companies.NotificationDeliveryLogs;

/// <summary>Fila de <c>admin.notification_delivery_logs</c> expuesta a la Application (HU #11363).</summary>
public sealed record NotificationDeliveryLogRecord(
    Guid Id,
    string TemplateKey,
    string Channel,
    string Recipient,
    string Result,
    string? FailureReason,
    int DurationMs,
    DateTimeOffset OccurredAt);

/// <summary>
/// Consulta de la bitácora de envíos, tenant-scoped (HU #11363, AC3). <b>Toda</b> consulta filtra
/// <c>tenant_id</c> de forma EXPLÍCITA: el RLS de <c>admin.notification_delivery_logs</c> es
/// decorativo (sin <c>FORCE ROW LEVEL SECURITY</c> y con la aplicación como owner, las políticas no
/// se evalúan) — mismo aviso que <c>ICompanyPersonalizedDocumentRepository</c>.
/// </summary>
public interface INotificationDeliveryLogRepository
{
    /// <summary>
    /// Más recientes primero (<c>occurred_at DESC</c>, mismo orden que el índice del DDL).
    /// <paramref name="take"/> se acota entre 1 y 200 por el handler antes de llegar aquí.
    /// </summary>
    Task<IReadOnlyList<NotificationDeliveryLogRecord>> ListByTenantAsync(
        Guid tenantId, int skip, int take, CancellationToken cancellationToken = default);
}
