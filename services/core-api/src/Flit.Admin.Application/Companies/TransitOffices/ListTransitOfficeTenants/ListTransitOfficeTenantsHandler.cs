using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficeTenants;

/// <summary>
/// Caso de uso del listado paginado de tenants OT. Normaliza la paginación y delega
/// la consulta server-side al repositorio, análogo a <c>ListCompaniesHandler</c>.
/// </summary>
public sealed class ListTransitOfficeTenantsHandler
{
    /// <summary>Página por defecto cuando no se informa o es inválida.</summary>
    public const int DefaultPage = 1;

    /// <summary>Tamaño de página por defecto.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tope de tamaño de página para proteger la base de datos.</summary>
    public const int MaxPageSize = 100;

    private readonly ITransitOfficeTenantWriteRepository _repository;

    public ListTransitOfficeTenantsHandler(ITransitOfficeTenantWriteRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListTransitOfficeTenantsResult> HandleAsync(
        ListTransitOfficeTenantsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var filter = new TransitOfficeTenantListFilter
        {
            LegalName = Trim(query.LegalName),
            EstadoActivo = query.EstadoActivo,
            Page = page,
            PageSize = pageSize,
        };

        var result = await _repository.ListAsync(filter, cancellationToken).ConfigureAwait(false);

        return new ListTransitOfficeTenantsResult(result.Items, result.TotalCount, page, pageSize);
    }

    private static int NormalizePage(int? page) =>
        page is null || page < 1 ? DefaultPage : page.Value;

    private static int NormalizePageSize(int? pageSize)
    {
        if (pageSize is null || pageSize < 1)
        {
            return DefaultPageSize;
        }

        return pageSize.Value > MaxPageSize ? MaxPageSize : pageSize.Value;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
