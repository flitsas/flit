namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Fila de la vista <c>admin.v_active_network_domains</c> (HU #12416, ADR-0060 D2): los únicos
/// dominios que resuelven red — <c>status = active</c>, vigentes (<c>deleted_at IS NULL</c>) y de una
/// cabeza <c>MARCA_BLANCA</c> con <c>is_group_parent</c> e <c>is_active</c>. Apagar la clase o
/// inactivar la cabeza la saca de aquí sin tocar <c>tenant_domains</c>. Solo lectura (keyless): la
/// consumen el resolutor de inquilino por host y la lista de orígenes CORS del Gateway.
/// </summary>
public sealed class ActiveNetworkDomainView
{
    public string Host { get; init; } = string.Empty;

    public Guid HeadTenantId { get; init; }
}
