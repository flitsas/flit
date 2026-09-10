namespace Flit.Admin.Application.Companies.ListCompanies;

/// <summary>
/// Petición del listado paginado de compañías (HU #10189, RF02).
/// Refleja los parámetros de query string del endpoint
/// <c>GET /api/v1/admin/companies/index</c>. Todos los filtros son opcionales.
/// </summary>
public sealed class ListCompaniesQuery
{
    public string? Nit { get; init; }

    public string? RazonSocial { get; init; }

    public bool? EstadoActivo { get; init; }

    public DateOnly? FechaDesde { get; init; }

    public DateOnly? FechaHasta { get; init; }

    /// <summary>
    /// Si es <c>true</c> (default cuando es nulo), excluye Organismos de Tránsito del listado.
    /// Pasar <c>false</c> para incluirlos (uso interno / retrocompatibilidad).
    /// </summary>
    public bool? ExcludeTransitOffices { get; init; }

    /// <summary>Página solicitada. Si es nula o &lt; 1 se normaliza a 1.</summary>
    public int? Page { get; init; }

    /// <summary>Tamaño de página. Si es nulo se usa el valor por defecto.</summary>
    public int? PageSize { get; init; }
}
