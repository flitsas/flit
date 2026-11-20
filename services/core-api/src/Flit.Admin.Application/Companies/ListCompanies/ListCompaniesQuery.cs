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

    /// <summary>Coincidencia parcial e insensible a mayúsculas sobre el código de compañía (COMP-XXXX). HU #13998.</summary>
    public string? Codigo { get; init; }

    /// <summary>
    /// Tipos de compañía a incluir (selección múltiple, OR). Cada elemento puede traer varios valores
    /// separados por coma (<c>RENTING,FLIT</c>) y el parámetro puede repetirse. Debe pertenecer al
    /// catálogo <c>CompanyTenantTypes</c>; si no, el handler devuelve errores de validación (422). HU #13998.
    /// </summary>
    public IReadOnlyList<string>? TenantType { get; init; }

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
