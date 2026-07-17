using Flit.Modules.Quipux.Domain.Consola;

namespace Flit.Modules.Quipux.Application.UseCases.Cola;

/// <summary>
/// Lista la cola Quipux (activa + histórico) de una secretaría destino para la consola del
/// SuperAdmin (HU #10774). Solo normaliza la paginación y delega la lectura —con su JOIN a los datos
/// del trámite— en el repositorio de consola.
/// </summary>
public sealed class ListarColaQuipuxHandler
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly IQuipuxSubmissionConsoleRepository _repository;

    public ListarColaQuipuxHandler(IQuipuxSubmissionConsoleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListarColaQuipuxResult> HandleAsync(
        ListarColaQuipuxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var result = await _repository
            .ListByTransitOfficeAsync(query.TransitOfficeId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return new ListarColaQuipuxResult(result.Items, result.TotalCount, page, pageSize);
    }

    private static int NormalizePage(int? page) =>
        page is null or < 1 ? DefaultPage : page.Value;

    private static int NormalizePageSize(int? pageSize)
    {
        if (pageSize is null or < 1)
        {
            return DefaultPageSize;
        }

        return pageSize.Value > MaxPageSize ? MaxPageSize : pageSize.Value;
    }
}

/// <summary>Página de la cola con eco de paginación para el cliente.</summary>
public sealed record ListarColaQuipuxResult(
    IReadOnlyList<QuipuxSubmissionQueueItem> Data,
    int TotalCount,
    int Page,
    int PageSize);
