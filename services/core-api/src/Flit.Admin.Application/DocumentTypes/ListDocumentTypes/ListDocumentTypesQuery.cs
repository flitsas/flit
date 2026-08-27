namespace Flit.Admin.Application.DocumentTypes.ListDocumentTypes;

/// <summary>
/// Petición del listado paginado del catálogo (HU #10193, AC2). Refleja los
/// parámetros de query string de <c>GET /api/v1/admin/document-types</c>.
/// </summary>
public sealed class ListDocumentTypesQuery
{
    /// <summary>Página solicitada. Si es nula o &lt; 1 se normaliza a 1.</summary>
    public int? Page { get; init; }

    /// <summary>Tamaño de página. Si es nulo se usa el valor por defecto.</summary>
    public int? PageSize { get; init; }

    /// <summary>Incluir inactivos (soft-deleted). Por defecto solo activos.</summary>
    public bool? IncludeInactive { get; init; }

    /// <summary>Filtro por código o nombre.</summary>
    public string? Search { get; init; }

    /// <summary><c>cargue</c> o <c>autogenerado</c>.</summary>
    public string? Origen { get; init; }

    /// <summary><c>activo</c> o <c>inactivo</c>.</summary>
    public string? Estado { get; init; }
}
