using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.SearchTransitOffices;

/// <summary>
/// Caso de uso del listado del catálogo estático de organismos de tránsito con
/// búsqueda opcional (HU #10192, AC1). Delega al catálogo en memoria — no consulta BD.
/// </summary>
public sealed class SearchTransitOfficesHandler
{
    private readonly ITransitOfficeCatalog _catalog;

    public SearchTransitOfficesHandler(ITransitOfficeCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public Task<IReadOnlyList<TransitOfficeResponse>> HandleAsync(
        SearchTransitOfficesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<TransitOfficeResponse> result =
        [
            .. _catalog.Search(query.Search)
                .Select(o => new TransitOfficeResponse(
                    o.Id, o.Code, o.Name, o.DepartmentCode, o.CityCode)),
        ];

        return Task.FromResult(result);
    }
}
