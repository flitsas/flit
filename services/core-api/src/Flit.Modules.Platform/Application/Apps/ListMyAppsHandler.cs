using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Security.Application.Products;

namespace Flit.Modules.Platform.Application.Apps;

/// <summary>
/// <c>GET /api/v1/platform/me/apps</c> (contrato v1 §6, HU #12966): los productos que el usuario puede
/// abrir, en el orden del catálogo. Base del menú de productos del hub y de la entrada directa (opción 4).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>La plataforma (el hub) aparece siempre: todo usuario con sesión puede abrirla.</item>
///   <item>Otro producto aparece si está encendido para la empresa (y su cabeza) y el usuario tiene rol
///   en él, según <see cref="IProductAccessResolver"/>.</item>
///   <item>Al SuperAdmin se le devuelven todos los productos activos (contrato §2.1).</item>
/// </list>
/// </remarks>
public sealed class ListMyAppsHandler
{
    private readonly IProductCatalog _catalog;
    private readonly IProductAccessResolver _resolver;
    private readonly IProductHosts _hosts;

    public ListMyAppsHandler(IProductCatalog catalog, IProductAccessResolver resolver, IProductHosts hosts)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _hosts = hosts ?? throw new ArgumentNullException(nameof(hosts));
    }

    public async Task<IReadOnlyList<MyAppView>> HandleAsync(
        Guid userId,
        Guid tenantId,
        bool isSuperAdmin,
        string? currentHost,
        CancellationToken ct)
    {
        var current = _hosts.ProductForHost(currentHost);
        var result = new List<MyAppView>();
        foreach (var product in await _catalog.ListAsync(ct).ConfigureAwait(false))
        {
            if (!product.IsActive)
                continue;

            if (!isSuperAdmin && product.Code != ProductCodes.Plataforma)
            {
                var access = await _resolver.ResolveAsync(userId, tenantId, product.Code, ct).ConfigureAwait(false);
                if (!access.ProductEnabled || access.Roles.Count == 0)
                    continue;
            }

            result.Add(new MyAppView(product.Code, product.Name, product.Icon, _hosts.UrlFor(product.Code), product.Code == current));
        }

        return result;
    }
}

/// <summary>Un producto del menú (contrato v1 §6: <c>{ code, name, icon, url, current }</c>).</summary>
public sealed record MyAppView(string Code, string Name, string Icon, string Url, bool Current);
