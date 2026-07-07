using Flit.Admin.Domain.Improntas;

namespace Flit.Admin.Application.Improntas.ListImprontas;

/// <summary>
/// Caso de uso del listado paginado del historial de improntas (HU #10468 / ADR-0022).
/// Normaliza la paginación y delega la consulta server-side al repositorio, mismo patrón
/// que <c>ListTransitOfficeTenantsHandler</c>.
/// </summary>
public sealed class ListImprontasHandler
{
    /// <summary>Página por defecto cuando no se informa o es inválida.</summary>
    public const int DefaultPage = 1;

    /// <summary>Tamaño de página por defecto.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tope de tamaño de página para proteger la base de datos (AC3 — nunca sin límite).</summary>
    public const int MaxPageSize = 100;

    private readonly IImprontaRepository _repository;

    public ListImprontasHandler(IImprontaRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListImprontasResult> HandleAsync(
        ListImprontasQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var filter = new ImprontaGenerationFilter
        {
            Placa = Trim(query.Placa),
            Radicado = Trim(query.Radicado),
            CreatedFrom = query.CreatedFrom,
            CreatedTo = query.CreatedTo,
            Page = page,
            PageSize = pageSize,
        };

        var result = await _repository.ListAsync(filter, cancellationToken).ConfigureAwait(false);

        return new ListImprontasResult(result.Items, result.TotalCount, page, pageSize);
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
