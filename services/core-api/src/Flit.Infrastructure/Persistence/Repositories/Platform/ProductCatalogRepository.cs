using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Modules.Platform.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories.Platform;

/// <summary>
/// Lectura de <c>platform.products</c> (HU #12958). Sin caché: son cinco filas y el resolutor de
/// acceso (B-05) tendrá la suya en Redis.
/// </summary>
internal sealed class ProductCatalogRepository : IProductCatalog
{
    private readonly FlitDbContext _context;

    public ProductCatalogRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.Set<ProductEntity>()
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task<Product?> FindAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var row = await _context.Set<ProductEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code, cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Map(row);
    }

    private static Product Map(ProductEntity e) => new(e.Code, e.Name, e.Icon, e.Status);
}
