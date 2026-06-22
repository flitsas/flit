using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies.TransitOffices;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura paginada del historial de auditoría de gobernanza de un tenant
/// (HU #10192, AC4) sobre <c>admin.tenant_config_audit_logs</c>. Solo lectura
/// (<c>AsNoTracking</c>), orden por <c>changed_at</c> descendente.
/// </summary>
internal sealed class TenantAuditLogRepository : ITenantAuditLogRepository
{
    private readonly FlitDbContext _context;

    public TenantAuditLogRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PagedResult<TenantAuditLogEntry>> ListPagedAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.TenantConfigAuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId);

        var totalCount = await baseQuery.LongCountAsync(cancellationToken).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<TenantAuditLogEntry>.Empty;
        }

        var items = await baseQuery
            .OrderByDescending(a => a.ChangedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new TenantAuditLogEntry(
                a.EntityName, a.FieldName, a.OldValue, a.NewValue, a.ChangedBy, a.ChangedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TenantAuditLogEntry>(items, totalCount);
    }
}
