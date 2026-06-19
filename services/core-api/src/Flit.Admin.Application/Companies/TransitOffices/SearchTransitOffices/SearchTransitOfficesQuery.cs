namespace Flit.Admin.Application.Companies.TransitOffices.SearchTransitOffices;

/// <summary>Consulta del catálogo estático de organismos de tránsito (AC1).</summary>
public sealed class SearchTransitOfficesQuery
{
    /// <summary>Término opcional de búsqueda (nombre o código). Nulo devuelve todo.</summary>
    public string? Search { get; init; }
}
