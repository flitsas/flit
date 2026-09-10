using Flit.Admin.Application.Companies.NotificationDeliveryLogs;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio EF Core de la bitácora de envíos (HU #11363). <b>Toda</b> consulta filtra
/// <c>tenant_id</c> de forma EXPLÍCITA en el <c>Where</c> — el RLS de
/// <c>admin.notification_delivery_logs</c> es decorativo (sin <c>FORCE ROW LEVEL SECURITY</c> y con
/// la aplicación como owner, las políticas no se evalúan), así que un filtro olvidado devolvería la
/// fila de cualquier tenant. Mismo patrón que <c>CompanyPersonalizedDocumentRepository</c>.
/// </summary>
internal sealed class NotificationDeliveryLogRepository : INotificationDeliveryLogRepository
{
    private readonly FlitDbContext _context;

    public NotificationDeliveryLogRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<IReadOnlyList<NotificationDeliveryLogRecord>> ListByTenantAsync(
        Guid tenantId, int skip, int take, CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var rows = await _context.NotificationDeliveryLogs
                    .AsNoTracking()
                    .Where(l => l.TenantId == tenantId)
                    .OrderByDescending(l => l.OccurredAt)
                    .ThenByDescending(l => l.Id)
                    .Skip(skip)
                    .Take(take)
                    .Select(l => new NotificationDeliveryLogRecord(
                        l.Id,
                        l.TemplateKey,
                        l.Channel,
                        l.Recipient,
                        l.Result,
                        l.FailureReason,
                        l.DurationMs,
                        l.OccurredAt))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<NotificationDeliveryLogRecord>)rows;
            },
            cancellationToken);
}
