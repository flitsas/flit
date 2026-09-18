namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Traduce la violación de <c>uq_tenant_domains_tenant_id</c> (la red ya tiene un dominio vigente,
/// DDL 116) a un error de negocio identificable (HU #12416 AC2). Defensa de carrera: el flujo normal
/// del repositorio retira el dominio anterior e inserta el nuevo en la MISMA transacción, así que esta
/// excepción solo aparece ante una escritura concurrente fuera de ese camino.
/// </summary>
public sealed class DomainAlreadyRegisteredForTenantException(Guid tenantId)
    : Exception($"El tenant {tenantId} ya tiene un dominio vigente.")
{
    public Guid TenantId { get; } = tenantId;
}
