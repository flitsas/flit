using Flit.Queries.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación de <see cref="ITenantScopeResolver"/> sobre <c>identity.tenants</c> (HU #12321).
/// Consulta <c>AsNoTracking</c> por petición, sin caché:
/// <list type="bullet">
///   <item>Interruptor <c>group_read_scope</c> apagado (HU #12323, <see cref="IHierarchySwitches"/>) ⇒
///   <see cref="TenantScope.Single"/> sin consultar la jerarquía: los datos (<c>is_group_parent</c>,
///   <c>parent_tenant_id</c>) quedan intactos y al reencender vuelve <c>Group</c> en la siguiente
///   petición, sin nuevo token.</item>
///   <item>Fila inexistente o <c>is_group_parent = false</c> ⇒ <see cref="TenantScope.Single"/>.</item>
///   <item><c>is_group_parent = true</c> ⇒ <see cref="TenantScope.Group"/> con TODOS los hijos
///   (<c>parent_tenant_id = tenantId</c>), incluidos los inactivos: desvincular ≠ desactivar, y el
///   padre debe seguir viendo el histórico de un hijo suspendido.</item>
///   <item>Cualquier excepción (BD caída, migración ausente, contexto liberado) ⇒ log Warning +
///   <see cref="TenantScope.Single"/>. Fail-closed: NUNCA <c>All</c>.</item>
/// </list>
/// </summary>
internal sealed partial class DbTenantScopeResolver : ITenantScopeResolver
{
    private readonly FlitDbContext _db;
    private readonly IHierarchySwitches _switches;
    private readonly ILogger<DbTenantScopeResolver> _logger;

    public DbTenantScopeResolver(
        FlitDbContext db,
        IHierarchySwitches switches,
        ILogger<DbTenantScopeResolver> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _switches = switches ?? throw new ArgumentNullException(nameof(switches));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("El identificador de tenant no puede ser Guid.Empty.", nameof(tenantId));

        try
        {
            // HU #12323 — interruptor global leído por petición: apagado ⇒ alcance propio, sin tocar datos.
            if (!await _switches.IsGroupReadScopeEnabledAsync(cancellationToken).ConfigureAwait(false))
                return TenantScope.Single(tenantId);

            var isGroupParent = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => (bool?)t.IsGroupParent)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (isGroupParent != true)
                return TenantScope.Single(tenantId);

            var children = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.ParentTenantId == tenantId)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return TenantScope.Group(tenantId, children);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ScopeResolutionFailed(_logger, tenantId, ex);
            return TenantScope.Single(tenantId);
        }
    }

    [LoggerMessage(
        EventId = 12321,
        Level = LogLevel.Warning,
        Message = "No se pudo resolver la jerarquía del tenant {TenantId}; se aplica alcance propio (fail-closed).")]
    private static partial void ScopeResolutionFailed(ILogger logger, Guid tenantId, Exception ex);
}
