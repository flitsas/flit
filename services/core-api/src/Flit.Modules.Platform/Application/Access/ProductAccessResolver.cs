using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Security.Application.Products;

namespace Flit.Modules.Platform.Application.Access;

/// <summary>
/// Implementación real de <see cref="IProductAccessResolver"/> (contrato de plataforma v1, §4; HU #12965,
/// B-05). La consume la emisión del token por producto (A-07) y la policy <c>RequireProduct</c> (B-06).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>plataforma</c> es el hub: siempre está encendido; los roles son los de plataforma.</item>
///   <item>Otro producto está encendido solo si lo está para la empresa y para cada uno de sus ancestros
///   (regla fail-closed de ADR-0057): apagar la cabeza apaga a sus hijas. Sin fila = apagado.</item>
///   <item>Roles y permisos: solo los del producto pedido (contrato §2).</item>
///   <item>Un código de producto desconocido se niega.</item>
///   <item>El SuperAdmin no pasa por aquí (bypass del contrato §2.1).</item>
/// </list>
/// Sin caché por ahora: la caché en Redis con invalidación por <c>platform.tenant_product.changed</c> y
/// <c>platform.roles.changed</c> llega cuando existan Redis (L-07) y el outbox (C-01).
/// </remarks>
public sealed class ProductAccessResolver : IProductAccessResolver
{
    private static readonly ProductAccess Denied = new(false, [], []);

    private readonly IProductAccessStore _store;

    public ProductAccessResolver(IProductAccessStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ProductAccess> ResolveAsync(Guid userId, Guid tenantId, string productCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productCode) || !ProductCodes.All.Contains(productCode, StringComparer.Ordinal))
            return Denied;

        var enabled = productCode == ProductCodes.Plataforma || await IsEnabledForChainAsync(tenantId, productCode, ct).ConfigureAwait(false);

        var grants = await _store.GetUserGrantsAsync(userId, tenantId, productCode, ct).ConfigureAwait(false);
        return new ProductAccess(
            enabled,
            grants.Roles.Select(r => new RoleRef(r.Id, r.Code)).ToList(),
            grants.Permissions);
    }

    private async Task<bool> IsEnabledForChainAsync(Guid tenantId, string productCode, CancellationToken ct)
    {
        var chain = await _store.GetTenantChainAsync(tenantId, ct).ConfigureAwait(false);
        if (chain.Count == 0)
            return false;

        var enabled = await _store.GetTenantsWithProductEnabledAsync(chain, productCode, ct).ConfigureAwait(false);
        return chain.All(enabled.Contains);
    }
}
