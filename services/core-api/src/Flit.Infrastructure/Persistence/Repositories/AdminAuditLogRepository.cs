using Flit.Admin.Domain.Auditing;
using Flit.Admin.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura paginada y GLOBAL (cross-tenant) del rastro unificado de auditoría
/// administrativo/seguridad (HU #10679) sobre <c>admin.tenant_config_audit_logs</c>.
/// Solo lectura (<c>AsNoTracking</c>), orden por <c>changed_at</c> descendente.
/// </summary>
/// <remarks>
/// RLS: la tabla tiene <c>tenant_isolation</c> por <c>app.current_tenant_id</c>, extendida
/// en HU #10678 con bypass SuperAdmin (<c>current_setting('app.is_superadmin', true) =
/// 'true'</c>). La consulta del SuperAdmin es cross-tenant por diseño (R1/R3 del
/// requerimiento), así que antes de leer se fija <c>app.is_superadmin='true'</c> dentro de
/// una transacción — mismo patrón de <c>set_config</c> por conexión que usa
/// <see cref="Auditing.AdminAuditWriter"/> — para que la policy la deje pasar sin filtrar
/// por tenant.
/// </remarks>
internal sealed class AdminAuditLogRepository : IAdminAuditLogRepository
{
    private readonly FlitDbContext _context;

    public AdminAuditLogRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PagedResult<AdminAuditLogEntry>> ListPagedAsync(
        AdminAuditLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    // Bypass RLS: la lectura es global (SuperAdmin), no se fija
                    // app.current_tenant_id — se marca app.is_superadmin para pasar la
                    // rama de bypass de la policy tenant_isolation (HU #10678).
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.is_superadmin', 'true', true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await QueryAsync(filter, cancellationToken).ConfigureAwait(false);

                    // Solo lectura: no hay cambios que confirmar, pero se cierra la
                    // transacción explícitamente para liberar la conexión de forma limpia.
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return result;
                }
            }).ConfigureAwait(false);
        }

        // Proveedor no relacional (InMemory, tests): RLS no aplica.
        return await QueryAsync(filter, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PagedResult<AdminAuditLogEntry>> QueryAsync(
        AdminAuditLogFilter filter,
        CancellationToken cancellationToken)
    {
        var baseQuery = _context.TenantConfigAuditLogs.AsNoTracking().AsQueryable();

        if (filter.UserId is { } userId)
        {
            // R2: matchea actor (changed_by) O afectado (target_entity_id).
            baseQuery = baseQuery.Where(a => a.ChangedBy == userId || a.TargetEntityId == userId);
        }

        if (filter.TenantId is { } tenantId)
        {
            baseQuery = baseQuery.Where(a => a.TenantId == tenantId);
        }

        if (filter.TenantType is { } tenantType)
        {
            baseQuery = baseQuery.Where(a => a.TenantType == tenantType);
        }

        if (filter.Module is { } module)
        {
            baseQuery = baseQuery.Where(a => a.Module == module);
        }

        if (filter.Operation is { } operation)
        {
            baseQuery = baseQuery.Where(a => a.Operation == operation);
        }

        if (filter.Result is { } result)
        {
            baseQuery = baseQuery.Where(a => a.Result == result);
        }

        if (filter.DateFrom is { } dateFrom)
        {
            baseQuery = baseQuery.Where(a => a.ChangedAt >= dateFrom);
        }

        if (filter.DateTo is { } dateTo)
        {
            baseQuery = baseQuery.Where(a => a.ChangedAt <= dateTo);
        }

        var totalCount = await baseQuery.LongCountAsync(cancellationToken).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<AdminAuditLogEntry>.Empty;
        }

        var items = await baseQuery
            .OrderByDescending(a => a.ChangedAt)
            .ThenByDescending(a => a.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(a => new AdminAuditLogEntry(
                a.Id,
                a.TenantId,
                a.TenantType,
                a.Module,
                a.EntityName,
                a.Operation,
                a.Result,
                a.ErrorCode,
                a.ChangedBy,
                a.TargetEntityType,
                a.TargetEntityId,
                a.ClientIp,
                a.ChangedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<AdminAuditLogEntry>(items, totalCount);
    }
}
