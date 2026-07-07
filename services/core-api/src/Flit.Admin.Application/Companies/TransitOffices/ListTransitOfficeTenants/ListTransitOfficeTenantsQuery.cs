namespace Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficeTenants;

/// <summary>
/// Petición del listado paginado de tenants OT. Refleja los parámetros de query
/// string del endpoint <c>GET /api/v1/admin/transit-office-tenants/index</c>. Todos
/// los filtros son opcionales.
/// </summary>
public sealed class ListTransitOfficeTenantsQuery
{
    public string? LegalName { get; init; }

    public bool? EstadoActivo { get; init; }

    /// <summary>Página solicitada. Si es nula o &lt; 1 se normaliza a 1.</summary>
    public int? Page { get; init; }

    /// <summary>Tamaño de página. Si es nulo se usa el valor por defecto.</summary>
    public int? PageSize { get; init; }
}
