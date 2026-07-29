using System.Diagnostics;
using System.Text.Json;
using Flit.Modules.Quipux.Domain.Codigos;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Puertos;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarEstado;

/// <summary>
/// Sondea en Quipux el estado de una submission radicada y reconcilia el trámite:
/// <c>estadoTramite.codigo = 2</c> ⇒ <c>entregado → aprobado</c>; <c>= 3</c> ⇒
/// <c>entregado → subsanacion</c> (HU #10871 AC2) con el motivo de la secretaría en el checklist
/// HÍBRIDO de <c>metadata</c> (sin ítems: Quipux no desglosa un checklist estructurado) — la
/// secretaría rechazó, pero es una observación SUBSANABLE: el gestor corrige y vuelve a radicar sin
/// perder el historial (antes de esta HU el destino era <c>rechazado</c>, terminal para este flujo).
/// </summary>
/// <remarks>
/// <para>
/// Dos correcciones respecto a FLIT 1.0:
/// </para>
/// <list type="bullet">
///   <item>
///   <b>"Sin resolver" ≠ 0.</b> Cuando Quipux responde sin <c>estadoTramite</c>, el trámite sigue en
///   curso y el sondeo es un no-op (solo se estampa). 1.0 colapsaba ese null a '0' y perdía la
///   distinción entre "aún no resuelto" y "respuesta sin estado".
///   </item>
///   <item>
///   <b>Atomicidad.</b> El cambio de estado de la submission y su evento van en el MISMO
///   <c>SaveChanges</c>, y la transición del trámite ocurre ANTES: si el proceso muere en medio, la
///   submission sigue <c>registrado</c> y el próximo sondeo converge. El orden inverso es el que
///   dejaba trámites huérfanos en 1.0.
///   </item>
/// </list>
/// </remarks>
public sealed class ConsultarEstadoQuipuxHandler
{
    /// <summary>
    /// Motivo por defecto del rechazo. <c>ITramiteLifecycleService</c> EXIGE <c>Reason</c> para
    /// <c>rechazado</c> (<c>motivo_requerido</c>): si Quipux rechaza sin descripción, dejar el motivo
    /// vacío haría fallar la transición en cada sondeo, para siempre.
    /// </summary>
    private const string MotivoRechazoPorDefecto =
        "Rechazado por el organismo de tránsito vía Quipux (sin descripción).";

    private readonly IQuipuxSettingsRepository _settings;
    private readonly IQuipuxSubmissionRepository _submissions;
    private readonly IQuipuxClient _client;
    private readonly IProcedureInstanceRepository _instances;
    private readonly ITramiteLifecycleService _lifecycle;
    private readonly IQuipuxAuditLog _audit;
    private readonly ILogger<ConsultarEstadoQuipuxHandler> _logger;

    public ConsultarEstadoQuipuxHandler(
        IQuipuxSettingsRepository settings,
        IQuipuxSubmissionRepository submissions,
        IQuipuxClient client,
        IProcedureInstanceRepository instances,
        ITramiteLifecycleService lifecycle,
        IQuipuxAuditLog audit,
        ILogger<ConsultarEstadoQuipuxHandler> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConsultarEstadoQuipuxResult> HandleAsync(
        ConsultarEstadoQuipuxCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var settings = await _settings.GetAsync(cancellationToken);
        if (settings is null || !settings.EstaCompleta())
        {
            return ConsultarEstadoQuipuxResult.Deshabilitado();
        }

        var submission = await _submissions.GetByIdAsync(command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return ConsultarEstadoQuipuxResult.NoEncontrado();
        }

        if (!submission.EstaRegistrada)
        {
            return ConsultarEstadoQuipuxResult.EstadoInvalido(submission.Id);
        }

        await _audit.WriteAsync(
            submission.TenantId, submission.Id, QuipuxStage.ConsultaEnviada, QuipuxOutcome.Ok,
            new { codigo_divipo = submission.DivipoCode, sondeo = submission.PollCount },
            command.CorrelationId, cancellationToken);

        // Instrumentación de duración (HU #10793): se mide el elapsed de la llamada HTTP para poder
        // mostrarlo en el LOG QX. Observabilidad, no cambio de comportamiento — no toca contrato,
        // reintentos ni timeouts. El origen es el worker de sondeo.
        var inicioConsulta = Stopwatch.GetTimestamp();

        QuipuxValidateStatusResult consulta;
        try
        {
            consulta = await _client.ValidateStatusAsync(
                new QuipuxValidateStatusRequest
                {
                    // Quipux correlaciona por nombre de documento + organismo; por eso el nombre se
                    // fija al encolar y no se recalcula nunca.
                    Documento = submission.DocumentName,
                    CodigoDivipo = submission.DivipoCode,
                    Consumidor = settings.ConsumerCode,
                },
                cancellationToken);
        }
        catch (QuipuxException ex)
        {
            var duracionError = ElapsedMs(inicioConsulta);

            // La ruta de error SÍ registra. En 1.0 saveValidateStatusIntegrationLogOnError leía
            // responseData sobre un DTO de request y lanzaba siempre: nunca dejó rastro de un fallo.
            await _audit.WriteAsync(
                submission.TenantId, submission.Id, QuipuxStage.ConsultaError,
                ex.IsTransient ? QuipuxOutcome.ErrorTransitorio : QuipuxOutcome.ErrorDefinitivo,
                new
                {
                    motivo = ConsultarEstadoQuipuxMotivos.LlamadaConsultaFallida,
                    transitorio = ex.IsTransient,
                    duration_ms = duracionError,
                    origen = QuipuxJobNames.StatusPoll,
                },
                command.CorrelationId, cancellationToken);

            ConsultarEstadoQuipuxLog.ConsultaFallida(_logger, ex, submission.Id, ex.IsTransient);

            // Se estampa el sondeo igualmente: si no, la ventana de frescura del claim volvería a
            // reclamar esta misma fila en el ciclo siguiente y el error se repetiría en bucle.
            await EstamparSondeoAsync(
                submission, QuipuxStage.ConsultaError, QuipuxOutcome.ErrorTransitorio,
                new { motivo = ConsultarEstadoQuipuxMotivos.LlamadaConsultaFallida },
                command, cancellationToken);

            return ConsultarEstadoQuipuxResult.ErrorConsulta(
                submission.Id, ConsultarEstadoQuipuxMotivos.LlamadaConsultaFallida);
        }

        await _audit.WriteAsync(
            submission.TenantId, submission.Id, QuipuxStage.ConsultaRespuesta, QuipuxOutcome.Ok,
            new
            {
                codigo = consulta.Codigo,
                estado_tramite = consulta.EstadoTramiteCodigo,
                duration_ms = ElapsedMs(inicioConsulta),
                origen = QuipuxJobNames.StatusPoll,
            },
            command.CorrelationId, cancellationToken);

        // Sin estadoTramite: Quipux aún no resolvió. NO se colapsa a 0 — ese era el fallo de 1.0.
        // Tampoco es terminal ningún otro código: solo 2 y 3 lo son.
        if (!QuipuxCodigos.EsEstadoTerminal(consulta.EstadoTramiteCodigo))
        {
            await EstamparSondeoAsync(
                submission, QuipuxStage.ConsultaRespuesta, QuipuxOutcome.Ok,
                new { estado_tramite = consulta.EstadoTramiteCodigo, sigue_en_tramite = true },
                command, cancellationToken);

            return ConsultarEstadoQuipuxResult.EnTramite(submission.Id);
        }

        return consulta.EstadoTramiteCodigo == QuipuxCodigos.TramiteAprobado
            ? await ResolverAprobadoAsync(submission, command, cancellationToken)
            : await ResolverRechazadoAsync(submission, consulta, command, cancellationToken);
    }

    private async Task<ConsultarEstadoQuipuxResult> ResolverAprobadoAsync(
        QuipuxSubmission submission,
        ConsultarEstadoQuipuxCommand command,
        CancellationToken cancellationToken)
    {
        var codigo = QuipuxCodigos.TramiteAprobado;

        var (ok, error) = await TransicionarAsync(
            submission, TramiteEstado.Aprobado, reason: null, metadata: null, cancellationToken);

        if (!ok)
        {
            return await TransicionFallidaAsync(submission, codigo, error, command, cancellationToken);
        }

        var ahora = DateTimeOffset.UtcNow;
        submission.Status = QuipuxSubmissionEstado.Aprobado;
        submission.QxProcedureCode = codigo;
        submission.CompletedAt = ahora;
        SellarSondeo(submission, ahora);

        await _submissions.UpdateAsync(
            submission,
            NuevoEvento(
                submission, QuipuxStage.Aprobado, QuipuxOutcome.Ok,
                new { estado_tramite = codigo }, command.CorrelationId, ahora),
            cancellationToken);

        ConsultarEstadoQuipuxLog.Aprobado(_logger, submission.Id, submission.ProcedureInstanceId);

        return ConsultarEstadoQuipuxResult.Aprobado(submission.Id, codigo);
    }

    private async Task<ConsultarEstadoQuipuxResult> ResolverRechazadoAsync(
        QuipuxSubmission submission,
        QuipuxValidateStatusResult consulta,
        ConsultarEstadoQuipuxCommand command,
        CancellationToken cancellationToken)
    {
        var codigo = QuipuxCodigos.TramiteRechazado;

        // El `reason` del historial es el equivalente 2.0 del rejectionObservation de 1.0: es lo que
        // el gestor lee para saber qué corregir antes de volver a radicar.
        var motivo = string.IsNullOrWhiteSpace(consulta.EstadoTramiteDescripcion)
            ? MotivoRechazoPorDefecto
            : consulta.EstadoTramiteDescripcion.Trim();

        // HU #10872 (AC1) — snapshot de field_values AL ENTRAR a subsanación: baseline del diff que
        // la re-radicación (TramiteLifecycleService) computará para re-evaluar solo los gates de lo
        // corregido. Proyección lean (GetFieldValuesAsync), sin cargar el resto del grafo del wizard.
        var fieldValues = await _instances.GetFieldValuesAsync(
            submission.ProcedureInstanceId, submission.TenantId, cancellationToken);

        // Checklist HÍBRIDO en metadata; destino `rechazado`. El operador activa edición con
        // POST /subsanar (flag). El snapshot sirve de baseline del diff de re-radicación.
        var metadata = new SubsanacionObservation
        {
            Motivo = motivo,
            FieldSnapshot = FieldValueSnapshot.Capture(fieldValues ?? []),
        }.ToJson();

        var (ok, error) = await TransicionarAsync(
            submission, TramiteEstado.Rechazado, motivo, metadata, cancellationToken);

        if (!ok)
        {
            return await TransicionFallidaAsync(submission, codigo, error, command, cancellationToken);
        }

        var ahora = DateTimeOffset.UtcNow;
        submission.Status = QuipuxSubmissionEstado.Rechazado;
        submission.QxProcedureCode = codigo;
        submission.RejectionReason = motivo;
        submission.CompletedAt = ahora;
        SellarSondeo(submission, ahora);

        await _submissions.UpdateAsync(
            submission,
            NuevoEvento(
                submission, QuipuxStage.Rechazado, QuipuxOutcome.Ok,
                // El motivo del rechazo lo redacta la secretaría y es visible para el gestor en el
                // historial del trámite; no se considera PII y se conserva para trazabilidad.
                new { estado_tramite = codigo, motivo }, command.CorrelationId, ahora),
            cancellationToken);

        ConsultarEstadoQuipuxLog.Rechazado(_logger, submission.Id, submission.ProcedureInstanceId);

        return ConsultarEstadoQuipuxResult.Rechazado(submission.Id, codigo, motivo);
    }

    /// <summary>
    /// Quipux resolvió pero el trámite no pudo transicionar. La submission se deja en
    /// <c>registrado</c>: el próximo sondeo vuelve a intentarlo. Marcarla resuelta aquí sería
    /// exactamente el huérfano de 1.0 — el trámite quedaría fuera de sincronía sin nadie que lo mire.
    /// </summary>
    private async Task<ConsultarEstadoQuipuxResult> TransicionFallidaAsync(
        QuipuxSubmission submission,
        int codigo,
        string? error,
        ConsultarEstadoQuipuxCommand command,
        CancellationToken cancellationToken)
    {
        ConsultarEstadoQuipuxLog.TransicionFallida(
            _logger, submission.ProcedureInstanceId, error ?? "desconocido");

        await EstamparSondeoAsync(
            submission, QuipuxStage.ConsultaError, QuipuxOutcome.ErrorTransitorio,
            new { motivo = ConsultarEstadoQuipuxMotivos.TransicionFallida, error, estado_tramite = codigo },
            command, cancellationToken);

        return ConsultarEstadoQuipuxResult.TransicionFallida(submission.Id, codigo, error);
    }

    /// <summary>
    /// Persiste el sondeo sin resolver la submission (sigue <c>registrado</c>), junto con su evento
    /// en el mismo <c>SaveChanges</c>.
    /// </summary>
    private async Task EstamparSondeoAsync(
        QuipuxSubmission submission,
        string stage,
        string outcome,
        object detalle,
        ConsultarEstadoQuipuxCommand command,
        CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        SellarSondeo(submission, ahora);

        await _submissions.UpdateAsync(
            submission,
            NuevoEvento(submission, stage, outcome, detalle, command.CorrelationId, ahora),
            cancellationToken);
    }

    /// <summary>
    /// Marca de sondeo. Estampar <c>last_polled_at</c> es lo que hace avanzar la ventana de frescura
    /// del claim: sin esto, <c>ClaimNextRegistradaAsync</c> reclamaría la misma fila en cada ciclo.
    /// Es idempotente (fija un instante), a diferencia de <c>poll_count</c>, cuyo incremento pertenece
    /// al claim — igual que <c>attempts</c> en el registrador.
    /// </summary>
    private static void SellarSondeo(QuipuxSubmission submission, DateTimeOffset ahora)
    {
        submission.LastPolledAt = ahora;
        submission.UpdatedAt = ahora;
    }

    /// <summary>
    /// Transición idempotente: si el trámite ya está en el destino (un ciclo anterior transicionó y
    /// murió antes de marcar la submission), no es un error.
    /// </summary>
    private async Task<(bool Ok, string? Error)> TransicionarAsync(
        QuipuxSubmission submission,
        string destino,
        string? reason,
        string? metadata,
        CancellationToken cancellationToken)
    {
        var instance = await _instances.GetByIdAsync(
            submission.ProcedureInstanceId, submission.TenantId, cancellationToken);

        if (instance is null)
        {
            return (false, TramiteEstadoErrores.NoEncontrado);
        }

        if (string.Equals(instance.Status, destino, StringComparison.Ordinal))
        {
            return (true, null);
        }

        var outcome = await _lifecycle.TransitionAsync(
            new TramiteTransitionCommand(
                submission.ProcedureInstanceId, submission.TenantId, destino, reason,
                ChangedByUserId: null, PlateFlowStatus: null, Metadata: metadata),
            cancellationToken);

        return outcome.Success ? (true, null) : (false, outcome.ErrorCode);
    }

    /// <summary>Milisegundos transcurridos desde un timestamp de <see cref="Stopwatch"/> (HU #10793).</summary>
    private static long ElapsedMs(long startTimestamp) =>
        (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private static QuipuxSubmissionEvent NuevoEvento(
        QuipuxSubmission submission,
        string stage,
        string outcome,
        object detalle,
        Guid? correlationId,
        DateTimeOffset ahora) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = submission.TenantId,
            SubmissionId = submission.Id,
            Stage = stage,
            Outcome = outcome,
            // Detalle SANITIZADO: códigos y el motivo del rechazo, nunca el response crudo.
            Detail = JsonSerializer.Serialize(detalle),
            CorrelationId = correlationId,
            OccurredAt = ahora,
        };
}

/// <summary>Logging source-generated (CA1848) del sondeo. Sin PII ni payloads.</summary>
internal static partial class ConsultarEstadoQuipuxLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Quipux: submission {SubmissionId} aprobada; trámite {InstanceId} → aprobado.")]
    public static partial void Aprobado(ILogger logger, Guid submissionId, Guid instanceId);

    // HU #10871 (AC2) — el destino del trámite es 'subsanacion' (observación subsanable), no
    // 'rechazado'; el nombre del método se conserva por lo que dijo Quipux (rechazo de secretaría).
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Quipux: submission {SubmissionId} rechazada por la secretaría; trámite {InstanceId} → subsanacion con motivo.")]
    public static partial void Rechazado(ILogger logger, Guid submissionId, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux: falló la consulta de estado de la submission {SubmissionId} (transitorio={Transitorio}).")]
    public static partial void ConsultaFallida(ILogger logger, Exception ex, Guid submissionId, bool transitorio);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux: el trámite {InstanceId} no pudo transicionar tras la resolución de Quipux ({Error}); se reintentará en el próximo sondeo.")]
    public static partial void TransicionFallida(ILogger logger, Guid instanceId, string error);
}
