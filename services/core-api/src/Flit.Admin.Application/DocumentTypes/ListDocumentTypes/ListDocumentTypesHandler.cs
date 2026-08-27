using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes.ListDocumentTypes;

/// <summary>
/// Caso de uso del listado paginado del catálogo de tipos de documento
/// (HU #10193, AC2 / RF02). Normaliza la paginación y delega la consulta
/// server-side (orden por nombre ascendente) al repositorio.
/// </summary>
public sealed class ListDocumentTypesHandler
{
    /// <summary>Página por defecto cuando no se informa o es inválida.</summary>
    public const int DefaultPage = 1;

    /// <summary>Tamaño de página por defecto.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tope de tamaño de página para proteger la base de datos.</summary>
    public const int MaxPageSize = 100;

    private readonly IDocumentTypeRepository _repository;

    public ListDocumentTypesHandler(IDocumentTypeRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListDocumentTypesResult> HandleAsync(
        ListDocumentTypesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var filter = new DocumentTypeListFilter
        {
            Page = page,
            PageSize = pageSize,
            IncludeInactive = (query.IncludeInactive ?? false) || ParseEstado(query.Estado) == false,
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            IsSystemGenerated = ParseOrigen(query.Origen),
            IsActive = ParseEstado(query.Estado),
        };

        var result = await _repository.ListAsync(filter, cancellationToken).ConfigureAwait(false);

        var data = result.Items
            .Select(DocumentTypeResponse.From)
            .ToList();

        return new ListDocumentTypesResult(data, result.TotalCount, page, pageSize);
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

    private static bool? ParseOrigen(string? origen) =>
        origen?.Trim().ToLowerInvariant() switch
        {
            "autogenerado" => true,
            "cargue" => false,
            _ => null,
        };

    private static bool? ParseEstado(string? estado) =>
        estado?.Trim().ToLowerInvariant() switch
        {
            "activo" => true,
            "inactivo" => false,
            _ => null,
        };
}
