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
        TenantAuditLogFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        // El aislamiento por tenant va SIEMPRE primero; los filtros solo restringen más.
        var baseQuery = _context.TenantConfigAuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId);

        if (filter is not null)
        {
            baseQuery = ApplyFilter(baseQuery, filter);
        }

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

    private IQueryable<Entities.Admin.TenantConfigAuditLog> ApplyFilter(
        IQueryable<Entities.Admin.TenantConfigAuditLog> query,
        TenantAuditLogFilter filter)
    {
        // Npgsql solo acepta DateTimeOffset en UTC para timestamptz: el handler arma los límites
        // con el offset de Bogotá (-05:00) y aquí se normalizan, sin cambiar el instante.
        if (filter.ChangedFromInclusive is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            query = query.Where(a => a.ChangedAt >= fromUtc);
        }

        if (filter.ChangedToExclusive is { } to)
        {
            var toUtc = to.ToUniversalTime();
            query = query.Where(a => a.ChangedAt < toUtc);
        }

        if (filter.Field is { } field)
        {
            var needle = field.ToLower();
            query = query.Where(a =>
                a.FieldName.ToLower().Contains(needle) || a.EntityName.ToLower().Contains(needle));
        }

        if (filter.OldValue is { } oldValue)
        {
            query = query.Where(OldValueContains(oldValue));
        }

        if (filter.Operator is { } op)
        {
            var needle = op.ToLower();
            // Id de usuario (lo que muestra hoy la columna), nombre o correo del usuario.
            query = query.Where(a =>
                a.ChangedBy != null
                && (a.ChangedBy.Value.ToString().ToLower().Contains(needle)
                    || _context.Users.Any(u =>
                        u.Id == a.ChangedBy
                        && (u.DisplayName.ToLower().Contains(needle) || u.Email.ToLower().Contains(needle)))));
        }

        return query;
    }

    /// <summary>
    /// «Contiene» sobre <c>old_value</c>, que es <c>jsonb</c>: PostgreSQL no tiene <c>lower(jsonb)</c>,
    /// así que se compara su forma de texto con <c>ILIKE</c> en una subconsulta. El proveedor
    /// InMemory de los tests de aplicación guarda el valor como texto y conserva el LINQ.
    /// </summary>
    private System.Linq.Expressions.Expression<Func<Entities.Admin.TenantConfigAuditLog, bool>> OldValueContains(
        string oldValue)
    {
        if (!_context.Database.IsNpgsql())
        {
            var needle = oldValue.ToLower();
            return a => a.OldValue != null && a.OldValue.ToLower().Contains(needle);
        }

        var pattern = "%" + oldValue.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
        var ids = _context.Database.SqlQuery<Guid>(
            $"""SELECT id AS "Value" FROM admin.tenant_config_audit_logs WHERE old_value::text ILIKE {pattern}""");
        return a => ids.Contains(a.Id);
    }
}
