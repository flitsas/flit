namespace Flit.Modules.Platform.Domain.Products;

/// <summary>
/// Lectura del catálogo <c>platform.products</c>. Lo siembra la migración de B-03 y lo amplía el
/// manifiesto de cada producto (B-06); ninguna pantalla lo edita.
/// </summary>
public interface IProductCatalog
{
    /// <summary>Todos los productos, en el orden del contrato §1.</summary>
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>El producto con ese código, o <c>null</c> si no existe.</summary>
    Task<Product?> FindAsync(string code, CancellationToken cancellationToken = default);
}
