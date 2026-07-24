using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.LegalRepresentatives.ListLegalRepresentatives;

/// <summary>
/// Caso de uso del listado paginado de representantes legales por tenant (HU #10901, AC1). Normaliza la
/// paginación (mismo criterio que el listado de compañías y el audit log) y delega la lectura
/// tenant-scoped (RLS) al reader. Devuelve el envelope <c>{ data, totalCount, page, pageSize }</c>.
/// </summary>
public sealed class ListLegalRepresentativesHandler
{
    /// <summary>Página por defecto cuando no se informa o es inválida.</summary>
    public const int DefaultPage = 1;

    /// <summary>Tamaño de página por defecto.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tope de tamaño de página para proteger la base de datos.</summary>
    public const int MaxPageSize = 100;

    private readonly ILegalRepresentativeReader _reader;

    public ListLegalRepresentativesHandler(ILegalRepresentativeReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<ListLegalRepresentativesResult> HandleAsync(
        ListLegalRepresentativesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var result = await _reader
            .ListPagedAsync(query.TenantId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<LegalRepresentativeResponse> data =
            [.. result.Items.Select(LegalRepresentativeResponse.From)];

        return new ListLegalRepresentativesResult(data, result.TotalCount, page, pageSize);
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
}
