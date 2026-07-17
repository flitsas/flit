namespace Flit.Modules.Quipux.Application.UseCases.ConsultarEstado;

/// <summary>
/// Orden de sondear en Quipux el estado de una submission ya radicada. La emite
/// <c>QuipuxStatusPollProcessor</c> tras reclamar la fila con la ventana de frescura.
/// </summary>
/// <remarks>
/// El sondeo es POLLING, no webhook: es la decisión explícita del usuario para esta integración
/// (el <c>OtTransitionSource.QuipuxWebhook</c> que ya existe en el repo corresponde a otro canal).
/// </remarks>
public sealed class ConsultarEstadoQuipuxCommand
{
    /// <summary>Submission a sondear. Debe estar en <c>registrado</c>.</summary>
    public required Guid SubmissionId { get; init; }

    /// <summary>Correlaciona los eventos de bitácora de este ciclo del worker.</summary>
    public Guid? CorrelationId { get; init; }
}

/// <summary>Desenlace de un sondeo.</summary>
public enum ConsultarEstadoQuipuxStatus
{
    /// <summary>
    /// Quipux aún no resolvió: <c>estadoTramite</c> ausente. Solo se estampa el sondeo. En 1.0 este
    /// caso se colapsaba a código '0' y se perdía la distinción entre "sin resolver" y "sin estado".
    /// </summary>
    EnTramite,

    /// <summary>La secretaría aprobó (<c>estadoTramite.codigo = 2</c>). Trámite <c>aprobado</c>.</summary>
    Aprobado,

    /// <summary>La secretaría rechazó (<c>estadoTramite.codigo = 3</c>). Trámite <c>rechazado</c> con motivo.</summary>
    Rechazado,

    /// <summary>Sin configuración completa (o <c>enabled = false</c>): no-op silencioso.</summary>
    Deshabilitado,

    /// <summary>La submission no existe.</summary>
    NoEncontrado,

    /// <summary>La submission no está en <c>registrado</c> (aún pendiente, o ya resuelta).</summary>
    EstadoInvalido,

    /// <summary>La consulta falló. La submission sigue siendo sondeable.</summary>
    ErrorConsulta,

    /// <summary>Quipux resolvió, pero el trámite no pudo transicionar. Se reintenta en el próximo sondeo.</summary>
    TransicionFallida,
}

/// <summary>Motivos estables de fallo del sondeo.</summary>
public static class ConsultarEstadoQuipuxMotivos
{
    /// <summary>La llamada de consulta falló o expiró.</summary>
    public const string LlamadaConsultaFallida = "llamada_consulta_fallida";

    /// <summary>Quipux resolvió el trámite pero <c>ITramiteLifecycleService</c> rechazó la transición.</summary>
    public const string TransicionFallida = "transicion_fallida";
}

/// <summary>Resultado tipado del sondeo. Sin excepciones para el flujo normal.</summary>
public sealed class ConsultarEstadoQuipuxResult
{
    public ConsultarEstadoQuipuxStatus Status { get; init; }

    public Guid? SubmissionId { get; init; }

    /// <summary>
    /// <c>estadoTramite.codigo</c> devuelto por Quipux. <b>null = Quipux aún no resolvió</b>, que NO
    /// es lo mismo que 0: mantener la distinción es explícitamente un fix respecto a 1.0.
    /// </summary>
    public int? EstadoTramiteCodigo { get; init; }

    /// <summary>Descripción del rechazo; se copia al <c>reason</c> del historial del trámite.</summary>
    public string? MotivoRechazo { get; init; }

    /// <summary>Código del motivo del fallo (ver <see cref="ConsultarEstadoQuipuxMotivos"/>); null si no hubo.</summary>
    public string? Motivo { get; init; }

    /// <summary>¿Quipux resolvió el trámite en este sondeo (aprobado o rechazado)?</summary>
    public bool EsTerminal =>
        Status is ConsultarEstadoQuipuxStatus.Aprobado or ConsultarEstadoQuipuxStatus.Rechazado;

    public static ConsultarEstadoQuipuxResult EnTramite(Guid submissionId) =>
        new() { Status = ConsultarEstadoQuipuxStatus.EnTramite, SubmissionId = submissionId };

    public static ConsultarEstadoQuipuxResult Aprobado(Guid submissionId, int codigo) =>
        new()
        {
            Status = ConsultarEstadoQuipuxStatus.Aprobado,
            SubmissionId = submissionId,
            EstadoTramiteCodigo = codigo,
        };

    public static ConsultarEstadoQuipuxResult Rechazado(Guid submissionId, int codigo, string? motivoRechazo) =>
        new()
        {
            Status = ConsultarEstadoQuipuxStatus.Rechazado,
            SubmissionId = submissionId,
            EstadoTramiteCodigo = codigo,
            MotivoRechazo = motivoRechazo,
        };

    public static ConsultarEstadoQuipuxResult Deshabilitado() =>
        new() { Status = ConsultarEstadoQuipuxStatus.Deshabilitado };

    public static ConsultarEstadoQuipuxResult NoEncontrado() =>
        new() { Status = ConsultarEstadoQuipuxStatus.NoEncontrado };

    public static ConsultarEstadoQuipuxResult EstadoInvalido(Guid submissionId) =>
        new() { Status = ConsultarEstadoQuipuxStatus.EstadoInvalido, SubmissionId = submissionId };

    public static ConsultarEstadoQuipuxResult ErrorConsulta(Guid submissionId, string motivo) =>
        new()
        {
            Status = ConsultarEstadoQuipuxStatus.ErrorConsulta,
            SubmissionId = submissionId,
            Motivo = motivo,
        };

    public static ConsultarEstadoQuipuxResult TransicionFallida(Guid submissionId, int codigo, string? error) =>
        new()
        {
            Status = ConsultarEstadoQuipuxStatus.TransicionFallida,
            SubmissionId = submissionId,
            EstadoTramiteCodigo = codigo,
            Motivo = error ?? ConsultarEstadoQuipuxMotivos.TransicionFallida,
        };
}
