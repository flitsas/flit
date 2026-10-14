using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Security.Application.Products;

namespace Flit.Modules.Platform.Application.Access;

/// <summary>
/// Productos encendidos para una empresa (Epic #13316, HU #13334): lo que responde
/// <c>IdentidadService.ObtenerProductosHabilitados</c> a los demás servicios. Misma regla que
/// <see cref="ProductAccessResolver"/>: <c>plataforma</c> siempre; cualquier otro, solo si está encendido para la
/// empresa y para cada uno de sus ancestros (fail-closed, ADR-0057). Una empresa que no existe, o cuya cadena no se
/// pudo leer completa, solo tiene <c>plataforma</c>.
/// </summary>
public sealed class ListEnabledProductsHandler
{
    private readonly IProductAccessStore _store;

    public ListEnabledProductsHandler(IProductAccessStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Códigos del contrato §1, ordenados.</summary>
    public async Task<IReadOnlyList<string>> HandleAsync(Guid tenantId, CancellationToken ct)
    {
        var enabled = new List<string> { ProductCodes.Plataforma };
        var chain = await _store.GetTenantChainAsync(tenantId, ct).ConfigureAwait(false);
        if (chain.Count > 0)
        {
            foreach (var product in ProductCodes.All.Where(p => p != ProductCodes.Plataforma))
            {
                var tenants = await _store.GetTenantsWithProductEnabledAsync(chain, product, ct).ConfigureAwait(false);
                if (chain.All(tenants.Contains))
                    enabled.Add(product);
            }
        }

        return [.. enabled.Order(StringComparer.Ordinal)];
    }
}
