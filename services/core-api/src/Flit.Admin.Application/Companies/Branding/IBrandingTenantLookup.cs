namespace Flit.Admin.Application.Companies.Branding;

/// <summary>
/// Estado ESTRUCTURAL mínimo de un tenant necesario para resolver identidad de marca (HU #12418
/// AC2/AC5/AC7): la clase (<c>tenant_type</c>), si es cabeza de grupo, si está activo y su
/// <c>parent_tenant_id</c>. Ninguno de estos campos es sensible por sí mismo (no hay NIT, correo ni
/// razón social) — igual se lee siempre en el servidor, nunca del token ni de un parámetro.
/// </summary>
public sealed record BrandingTenantSnapshot(
    Guid TenantId,
    string TenantType,
    bool IsGroupParent,
    bool IsActive,
    Guid? ParentTenantId);

/// <summary>
/// Lectura directa de <c>identity.tenants</c> para resolución de marca (HU #12418). Implementación
/// EF Core en <c>Flit.Infrastructure.Persistence.Repositories.BrandingTenantLookupRepository</c>.
/// Se usa en lectura, sin caché propia, como verificación de última hora de que la cabeza SIGUE
/// siendo MARCA_BLANCA + cabeza de grupo + activa (AC2/AC7 "apagar la clase invalida"), incluso si
/// el <c>DomainContext</c> de la petición viniera de una resolución cacheada por
/// <c>ITenantDomainResolver</c>.
/// </summary>
public interface IBrandingTenantLookup
{
    /// <summary><c>null</c> si el tenant no existe.</summary>
    Task<BrandingTenantSnapshot?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
