using System.Text.Json;
using Flit.Modules.Quipux.Domain.LogQx;
using Flit.Modules.Quipux.Domain.Trazabilidad;

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
/// solo el <c>detail</c> ya sanitizado en captura. El enmascarado adicional de datos sensibles es
/// alcance de la HU #10794 (una segunda barrera sobre esta proyección).
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

        var domainQuery = new QuipuxLogQuery(
            Trim(query.Placa),
            query.ProcedureInstanceId,
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

    private static QuipuxLogEventView MapEvent(QuipuxLogEvent ev)
    {
        JsonElement? detail = null;
        long? durationMs = null;
        string? origin = null;
        int? responseCode = null;

        if (!string.IsNullOrWhiteSpace(ev.Detail))
        {
            try
            {
                using var doc = JsonDocument.Parse(ev.Detail);
                // Clonar: el JsonDocument se libera al salir del using, pero el JsonElement clonado
                // sobrevive y se serializa tal cual (el detail ya viene sanitizado desde captura).
                var root = doc.RootElement.Clone();
                detail = root;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("duration_ms", out var d)
                        && d.ValueKind == JsonValueKind.Number
                        && d.TryGetInt64(out var ms))
                    {
                        durationMs = ms;
                    }

                    if (root.TryGetProperty("origen", out var o) && o.ValueKind == JsonValueKind.String)
                    {
                        origin = o.GetString();
                    }

                    if (root.TryGetProperty("codigo", out var c)
                        && c.ValueKind == JsonValueKind.Number
                        && c.TryGetInt32(out var code))
                    {
                        responseCode = code;
                    }
                }
            }
            catch (JsonException)
            {
                // Detail histórico no-JSON: se trata como "sin payload disponible", sin romper la página.
                detail = null;
            }
        }

        origin ??= DeriveOrigin(ev.Stage);

        return new QuipuxLogEventView(
            ev.Stage,
            ev.Outcome,
            detail,
            durationMs,
            origin,
            responseCode,
            ev.CorrelationId,
            ev.OccurredAt);
    }

    /// <summary>
    /// Origen best-effort para eventos previos a la instrumentación (sin <c>origen</c> en el detail):
    /// las etapas <c>registro_*</c> las genera el worker registrador; las <c>consulta_*</c>, el sondeo.
    /// Los eventos nuevos traen el origen explícito y esta derivación no aplica.
    /// </summary>
    private static string? DeriveOrigin(string stage)
    {
        if (stage.StartsWith("registro", StringComparison.Ordinal))
        {
            return QuipuxJobNames.Register;
        }

        return stage.StartsWith("consulta", StringComparison.Ordinal) ? QuipuxJobNames.StatusPoll : null;
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
