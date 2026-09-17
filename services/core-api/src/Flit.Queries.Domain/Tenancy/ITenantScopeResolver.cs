namespace Flit.Queries.Domain.Tenancy;

/// <summary>
/// Calcula el <see cref="TenantScope"/> de un cliente a partir de la jerarquía persistida
/// (HU #12321). Contrato cerrado por defecto: la implementación devuelve <see cref="TenantScope.Single"/>
/// ante cualquier fallo o ausencia de datos y NUNCA <c>All</c> (esa fábrica es <c>internal</c>).
/// </summary>
public interface ITenantScopeResolver
{
    /// <summary>Alcance del tenant <paramref name="tenantId"/> (propio o cabeza de grupo con sus hijos).</summary>
    Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
