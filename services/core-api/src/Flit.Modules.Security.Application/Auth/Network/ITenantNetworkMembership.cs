namespace Flit.Modules.Security.Application.Auth.Network;

/// <summary>
/// Resultado de resolver la pertenencia de un tenant a una red MARCA_BLANCA (HU #12422, ADR-0060 D3).
/// <see cref="HeadTenantId"/> es la cabeza MARCA_BLANCA activa de la que <c>tenantId</c> es miembro:
/// la propia cabeza (si <c>tenantId</c> ES la cabeza) o su padre (si <c>tenantId</c> es un hijo de esa
/// cabeza). <see cref="IsMarcaBlancaNetwork"/> es <c>false</c> para Concesión, compañías sin red o
/// cabezas inactivas — nunca se confunde con Concesión (fail-closed, nunca una red distinta).
/// </summary>
public readonly record struct NetworkMembership(Guid? HeadTenantId, bool IsMarcaBlancaNetwork, string? ActiveHost)
{
    public static NetworkMembership None { get; } = new(null, false, null);
}

/// <summary>
/// Punto único de resolución de pertenencia a una red MARCA_BLANCA (HU #12422, ADR-0060 D3). Se apoya
/// en el mismo dato estructural que <c>ITenantScopeResolver</c> (<c>identity.tenants.tenant_type</c> +
/// <c>parent_tenant_id</c>, HU #12321/#12406) — nunca datos del cliente ni el token. Implementación
/// EF Core en <c>Flit.Infrastructure.Persistence.DbTenantNetworkMembership</c>, que también resuelve
/// el dominio activo de la cabeza (fusiona lo que hubiera sido <c>INetworkDomainLookup</c>: es la misma
/// consulta y separar las dos interfaces solo duplicaba la lectura de <c>admin.v_active_network_domains</c>).
/// </summary>
public interface ITenantNetworkMembership
{
    /// <summary>
    /// Resuelve la pertenencia de <paramref name="tenantId"/> a una red MARCA_BLANCA. Fail-closed:
    /// cualquier fallo o dato inconsistente resuelve a <see cref="NetworkMembership.None"/>, nunca lanza.
    /// </summary>
    Task<NetworkMembership> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
