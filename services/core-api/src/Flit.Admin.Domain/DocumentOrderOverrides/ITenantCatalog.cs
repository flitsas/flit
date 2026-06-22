namespace Flit.Admin.Domain.DocumentOrderOverrides;

/// <summary>
/// Consulta de solo lectura sobre el catálogo de clientes/tenants
/// (<c>identity.tenants</c>). HU #10196 únicamente necesita comprobar la existencia de un
/// cliente al registrar un override de scope <c>CLIENTE</c>.
/// </summary>
public interface ITenantCatalog
{
    /// <summary>True si existe un cliente (tenant) con el id indicado.</summary>
    Task<bool> ExistsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
