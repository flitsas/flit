using Flit.Modules.Quipux.Domain.LogQx;

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarLog;

/// <summary>
/// Consulta el LOG QX de la integración Quipux (HU #10793): dada una placa, un id de trámite o un
/// radicado, devuelve la(s) radicación(es) con su línea de tiempo completa. Normaliza la paginación,
/// delega el JOIN + lectura de eventos en el repositorio, y proyecta cada evento extrayendo de su
/// <c>detail</c> sanitizado la duración de la llamada HTTP, el origen (worker) y el código de
/// respuesta cuando existen.
/// </summary>
/// <remarks>
/// Es un caso de uso de SOLO LECTURA: no escribe, no transiciona y nunca devuelve payload crudo —
/// solo el <c>detail</c> ya sanitizado en captura, sobre el que además se aplica el enmascarado de
/// datos sensibles de la HU #10794 (<see cref="LogQxSensitiveDataMasker"/>) como segunda barrera.
/// </remarks>
public sealed class ConsultarLogQuipuxHandler
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly IQuipuxLogRepository _repository;

    public ConsultarLogQuipuxHandler(IQuipuxLogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ConsultarLogQuipuxResult> HandleAsync(
        ConsultarLogQuipuxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        // instanceId llega como texto libre (el borde HTTP no revienta con 400 ante un valor no-UUID):
        // si viene y no parsea a Guid, no puede casar con ninguna radicación → página vacía, mismo
        // criterio que un radicado no numérico. Ausente/vacío = sin filtro por trámite.
        Guid? instanceId = null;
        var rawInstance = Trim(query.InstanceId);
        if (rawInstance is not null)
        {
            if (!Guid.TryParse(rawInstance, out var parsed))
            {
                return new ConsultarLogQuipuxResult([], 0, page, pageSize);
            }

            instanceId = parsed;
        }

        var domainQuery = new QuipuxLogQuery(
            Trim(query.Placa),
            instanceId,
            Trim(query.Radicado),
            page,
            pageSize);

        var result = await _repository.SearchAsync(domainQuery, cancellationToken).ConfigureAwait(false);

        var data = result.Entries.Select(MapEntry).ToList();
        return new ConsultarLogQuipuxResult(data, result.TotalCount, page, pageSize);
    }

    private static QuipuxLogEntryView MapEntry(QuipuxLogEntry e) =>
        new(
            e.Id,
            e.ProcedureInstanceId,
            e.ReferenceNumber,
            e.ProcedureTypeName,
            e.ClientTenantName,
            e.Plate,
            e.DocumentName,
            e.DivipoCode,
            e.Status,
            e.Attempts,
            e.PollCount,
            e.QxRegisterCode,
            e.QxProcedureCode,
            e.RejectionReason,
            e.CreatedAt,
            e.RegisteredAt,
            e.LastPolledAt,
            e.CompletedAt,
            e.UpdatedAt,
            e.Events.Select(MapEvent).ToList());

    /// <summary>
    /// Proyecta el evento delegando el enmascarado y la extracción de campos técnicos en
    /// <see cref="LogQxDetailProjector"/>, compartido con el log completo de la HU #11787.
    /// </summary>
    private static QuipuxLogEventView MapEvent(QuipuxLogEvent ev)
    {
        var p = LogQxDetailProjector.Project(ev.Detail, ev.Stage);

        return new QuipuxLogEventView(
            ev.Stage,
            ev.Outcome,
            p.Detail,
            p.DurationMs,
            p.Origin,
            p.ResponseCode,
            ev.CorrelationId,
            ev.OccurredAt);
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
