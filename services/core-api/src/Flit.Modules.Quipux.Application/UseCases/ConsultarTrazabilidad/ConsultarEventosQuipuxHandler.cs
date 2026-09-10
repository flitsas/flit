using Flit.Modules.Quipux.Application.UseCases.ConsultarLog;
using Flit.Modules.Quipux.Domain.LogQx;

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarTrazabilidad;

/// <summary>Filtro del log completo tal como llega del borde HTTP.</summary>
public sealed record ConsultarEventosQuipuxQuery(
    Guid SubmissionId,
    bool? OcultarSinNovedad,
    bool? SoloErrores,
    int? Page,
    int? PageSize);

/// <summary>
/// Log completo de una radicación (HU #11787): todos sus eventos, filtrados y paginados en servidor.
/// </summary>
/// <remarks>
/// <para>El interruptor de ocultar consultas viene ACTIVO por defecto: es lo que convierte las 1.065
/// filas del caso de referencia en las cinco que dicen algo. Desactivarlo devuelve la totalidad —
/// no se pierde ni un registro, solo deja de mostrarse por defecto.</para>
/// <para>Solo lectura, y el <c>detail</c> se devuelve enmascarado con la misma barrera que la
/// búsqueda de la HU #10793 (<see cref="LogQxDetailProjector"/>).</para>
/// </remarks>
public sealed class ConsultarEventosQuipuxHandler
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    /// <summary>Por defecto se ocultan los sondeos sin novedad; hay que pedir verlos.</summary>
    public const bool DefaultOcultarSinNovedad = true;

    private readonly IQuipuxTrazabilidadRepository _repository;

    public ConsultarEventosQuipuxHandler(IQuipuxTrazabilidadRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>Devuelve <c>null</c> si la radicación no existe — el borde lo traduce a 404.</summary>
    public async Task<ConsultarEventosQuipuxResult?> HandleAsync(
        ConsultarEventosQuipuxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Se comprueba la existencia antes de listar: sin esto, una radicación inexistente y una sin
        // eventos darían la misma respuesta vacía y no se podrían distinguir.
        var radicacion = await _repository
            .GetRadicacionAsync(query.SubmissionId, cancellationToken)
            .ConfigureAwait(false);

        if (radicacion is null)
        {
            return null;
        }

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var result = await _repository
            .ListEventosAsync(
                new QuipuxEventosQuery(
                    query.SubmissionId,
                    query.OcultarSinNovedad ?? DefaultOcultarSinNovedad,
                    query.SoloErrores ?? false,
                    page,
                    pageSize),
                cancellationToken)
            .ConfigureAwait(false);

        var data = result.Eventos.Select(MapEvento).ToList();

        return new ConsultarEventosQuipuxResult(
            data,
            result.TotalCount,
            page,
            pageSize,
            result.OcultosSinNovedad,
            result.TotalEventos);
    }

    private static QuipuxEventoView MapEvento(QuipuxEventoDetallado e)
    {
        var p = LogQxDetailProjector.Project(e.Detail, e.Stage);

        return new QuipuxEventoView(
            e.Stage,
            e.Outcome,
            p.Detail,
            p.DurationMs,
            p.Origin,
            p.ResponseCode,
            e.CorrelationId,
            e.OccurredAt);
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
