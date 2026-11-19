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

        var keys = filter.FieldKeys is { Count: > 0 } k ? k.ToList() : null;
        if (filter.Field is { } field)
        {
            var needle = field.ToLower();
            query = keys is null
                ? query.Where(a =>
                    a.FieldName.ToLower().Contains(needle) || a.EntityName.ToLower().Contains(needle))
                : query.Where(a =>
                    a.FieldName.ToLower().Contains(needle) || a.EntityName.ToLower().Contains(needle)
                    || keys.Contains(a.FieldName.ToLower()) || keys.Contains(a.EntityName.ToLower()));
        }
        else if (keys is not null)
        {
            query = query.Where(a => keys.Contains(a.FieldName.ToLower()) || keys.Contains(a.EntityName.ToLower()));
        }

        var oldTerms = new List<string>();
        if (filter.OldValue is { } oldValue)
        {
            oldTerms.Add(oldValue);
        }

        if (filter.OldValueAlternatives is { Count: > 0 } alts)
        {
            oldTerms.AddRange(alts);
        }

        if (oldTerms.Count > 0)
        {
            query = query.Where(OldValueContainsAny(oldTerms));
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
    private System.Linq.Expressions.Expression<Func<Entities.Admin.TenantConfigAuditLog, bool>> OldValueContainsAny(
        List<string> terms)
    {
        if (!_context.Database.IsNpgsql())
        {
            // OR armado como árbol de expresiones: InMemory no traduce Any() con lambda anidada.
            var param = System.Linq.Expressions.Expression.Parameter(typeof(Entities.Admin.TenantConfigAuditLog), "a");
            var oldValue = System.Linq.Expressions.Expression.Property(param, nameof(Entities.Admin.TenantConfigAuditLog.OldValue));
            var toLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
            var contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
            var lowered = System.Linq.Expressions.Expression.Call(oldValue, toLower);
            System.Linq.Expressions.Expression body = System.Linq.Expressions.Expression.Constant(false);
            foreach (var term in terms)
            {
                body = System.Linq.Expressions.Expression.OrElse(
                    body,
                    System.Linq.Expressions.Expression.Call(
                        lowered, contains, System.Linq.Expressions.Expression.Constant(term.ToLower())));
            }

            var notNull = System.Linq.Expressions.Expression.NotEqual(
                oldValue, System.Linq.Expressions.Expression.Constant(null, typeof(string)));
            return System.Linq.Expressions.Expression.Lambda<Func<Entities.Admin.TenantConfigAuditLog, bool>>(
                System.Linq.Expressions.Expression.AndAlso(notNull, body), param);
        }

        var patterns = terms
            .Select(t => "%" + t.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%")
            .ToArray();
        var ids = _context.Database.SqlQuery<Guid>(
            $"""SELECT id AS "Value" FROM admin.tenant_config_audit_logs WHERE old_value::text ILIKE ANY ({patterns})""");
        return a => ids.Contains(a.Id);
    }
}
