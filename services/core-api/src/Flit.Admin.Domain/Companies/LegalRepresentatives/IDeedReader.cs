using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.Companies.LegalRepresentatives;

/// <summary>
/// Lecturas de escrituras (HU #10900, ADR-0033). Tenant-scoped: cada lectura corre bajo el contexto
/// RLS del tenant (<c>app.current_tenant_id</c>).
/// </summary>
public interface IDeedReader
{
    /// <summary>Listado paginado de escrituras del tenant (con sus compañías).</summary>
    Task<PagedResult<DeedItem>> ListPagedAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Una escritura por id dentro del tenant. <c>null</c> si no existe.</summary>
    Task<DeedItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Escrituras activas y VIGENTES del tenant en <paramref name="today"/> (collapse del primer paso
    /// del wizard). Ordenadas por vencimiento más próximo primero.
    /// </summary>
    Task<IReadOnlyList<DeedItem>> ListActiveVigentesAsync(
        Guid tenantId,
        DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>Escritura activa ligada a la ficha de compañía; <c>null</c> si no hay.</summary>
    Task<DeedItem?> FindActiveByCompanyAsync(
        Guid tenantId,
        Guid representedCompanyId,
        CancellationToken cancellationToken = default);
}
