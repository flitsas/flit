using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;

namespace Flit.Admin.Application.Companies.ListCompanies;

/// <summary>
/// Caso de uso del listado paginado de compañías (HU #10189, RF02).
/// Normaliza la paginación, traduce filtros opcionales y delega la consulta
/// server-side al repositorio de solo lectura.
/// </summary>
public sealed class ListCompaniesHandler
{
    /// <summary>Página por defecto cuando no se informa o es inválida.</summary>
    public const int DefaultPage = 1;

    /// <summary>Tamaño de página por defecto.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tope de tamaño de página para proteger la base de datos.</summary>
    public const int MaxPageSize = 100;

    private readonly ICompanyReadRepository _repository;

    public ListCompaniesHandler(ICompanyReadRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListCompaniesResult> HandleAsync(
        ListCompaniesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tenantTypes = ParseTenantTypes(query.TenantType, out var invalid);
        if (invalid.Count > 0)
        {
            return ListCompaniesResult.Invalid(
            [
                new CompanyValidationError(
                    "tenantType",
                    $"Tipo de compañía no válido ({string.Join(", ", invalid)}). Debe ser {CompanyTenantTypes.DisplayList}."),
            ]);
        }

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var filter = new CompanyListFilter
        {
            Nit = Trim(query.Nit),
            RazonSocial = Trim(query.RazonSocial),
            Codigo = Trim(query.Codigo),
            TenantTypes = tenantTypes,
            EstadoActivo = query.EstadoActivo,
            FechaDesde = query.FechaDesde,
            FechaHasta = query.FechaHasta,
            // Default true: el admin de compañías B2B no lista Organismos de Tránsito.
            ExcludeTransitOffices = query.ExcludeTransitOffices ?? true,
            Page = page,
            PageSize = pageSize,
        };

        var result = await _repository.ListAsync(filter, cancellationToken).ConfigureAwait(false);

        return new ListCompaniesResult(result.Items, result.TotalCount, page, pageSize);
    }

    private static List<string>? ParseTenantTypes(IReadOnlyList<string>? raw, out List<string> invalid)
    {
        invalid = [];
        if (raw is null)
        {
            return null;
        }

        var valid = new List<string>();
        foreach (var token in raw
                     .Where(r => !string.IsNullOrWhiteSpace(r))
                     .SelectMany(r => r.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            var normalized = token.ToUpperInvariant();
            if (CompanyTenantTypes.IsValid(normalized))
            {
                if (!valid.Contains(normalized))
                {
                    valid.Add(normalized);
                }
            }
            else if (!invalid.Contains(token))
            {
                invalid.Add(token);
            }
        }

        return valid.Count == 0 ? null : valid;
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
