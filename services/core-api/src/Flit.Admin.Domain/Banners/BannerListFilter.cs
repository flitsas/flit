namespace Flit.Admin.Domain.Banners;

/// <summary>Paginación del listado administrable de banners (HU #12239, AC2). Orden fijo: más reciente primero.</summary>
public sealed class BannerListFilter
{
    /// <summary>Página solicitada (1-based, ya normalizada).</summary>
    public required int Page { get; init; }

    /// <summary>Tamaño de página (ya normalizado dentro de límites válidos).</summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// <c>true</c> incluye también los banners con soft-delete (<c>deleted_at</c> no nulo).
    /// Por defecto el listado solo devuelve los no eliminados (regla estándar del repo).
    /// </summary>
    public bool IncludeDeleted { get; init; }
}
