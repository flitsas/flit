using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Modules.Security.Application.Products;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// Productos de una empresa tal como los ve el SuperAdmin: todo el catálogo salvo
/// <c>plataforma</c>, con su estado para esa empresa. Un producto sin fila sale apagado
/// (fail-closed). Base de <c>GET /admin/tenants/{tenantId}/products</c> (B-06).
/// </summary>
public sealed class ListTenantProductsHandler
{
    private readonly IProductCatalog _catalog;
    private readonly ITenantProductRepository _repository;

    public ListTenantProductsHandler(IProductCatalog catalog, ITenantProductRepository repository)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<TenantProductView>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var products = await _catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var rows = (await _repository.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => r.ProductCode, StringComparer.Ordinal);

        return products
            .Where(p => p.Code != ProductCodes.Plataforma)
            .Select(p => rows.TryGetValue(p.Code, out var row)
                ? new TenantProductView(p.Code, p.Name, row.Enabled, row.Notes, row.UpdatedAt, row.UpdatedBy)
                : new TenantProductView(p.Code, p.Name, false, null, null, null))
            .ToList();
    }
}

/// <summary>Un producto del catálogo con su estado para la empresa consultada.</summary>
public sealed record TenantProductView(
    string ProductCode,
    string Name,
    bool Enabled,
    string? Notes,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy);
