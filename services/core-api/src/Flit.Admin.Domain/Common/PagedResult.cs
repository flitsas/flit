namespace Flit.Admin.Domain.Common;

/// <summary>
/// Resultado de una consulta paginada server-side. Inmutable.
/// </summary>
/// <typeparam name="T">Tipo de cada elemento del listado.</typeparam>
public sealed class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, long totalCount)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        TotalCount = totalCount;
    }

    /// <summary>Elementos de la página solicitada.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Total de elementos que cumplen el filtro (antes de paginar).</summary>
    public long TotalCount { get; }

    /// <summary>Página vacía reutilizable cuando no hay resultados.</summary>
    public static PagedResult<T> Empty { get; } = new([], 0);
}
