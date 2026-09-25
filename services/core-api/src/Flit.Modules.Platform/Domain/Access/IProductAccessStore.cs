namespace Flit.Modules.Platform.Domain.Access;

/// <summary>
/// Lecturas que necesita el resolutor de acceso a productos (HU #12965, B-05). Las implementa
/// <c>Flit.Infrastructure</c> sobre <c>identity.tenants</c>, <c>platform.tenant_products</c> y el RBAC.
/// </summary>
public interface IProductAccessStore
{
    /// <summary>
    /// La empresa y sus ancestros por <c>parent_tenant_id</c>, empezando por ella. Una empresa inexistente
    /// devuelve lista vacía.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetTenantChainAsync(Guid tenantId, CancellationToken ct);

    /// <summary>De <paramref name="tenantIds"/>, las que tienen <paramref name="productCode"/> encendido.</summary>
    Task<IReadOnlySet<Guid>> GetTenantsWithProductEnabledAsync(IReadOnlyList<Guid> tenantIds, string productCode, CancellationToken ct);

    /// <summary>
    /// Roles activos del usuario en la empresa que son de <paramref name="productCode"/>, y los permisos
    /// activos de esos roles que pertenecen a módulos del mismo producto.
    /// </summary>
    Task<UserProductGrants> GetUserGrantsAsync(Guid userId, Guid tenantId, string productCode, CancellationToken ct);
}

/// <summary>Roles y permisos de un usuario en un producto.</summary>
public sealed record UserProductGrants(IReadOnlyList<(Guid Id, string Code)> Roles, IReadOnlyList<string> Permissions);
