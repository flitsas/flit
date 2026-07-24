namespace Flit.Modules.Quipux.Domain.Trazabilidad;

/// <summary>
/// Evento de la bitácora de una radicación (<c>tramites.quipux_submission_events</c>). Da la
/// trazabilidad completa "de la ejecución a la finalización" de cada trámite. Modela
/// <c>IdentityValidationAuditEvent</c>, el precedente del repo.
/// </summary>
public sealed class QuipuxSubmissionEvent
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid SubmissionId { get; set; }

    /// <summary>Ver <see cref="QuipuxStage"/>.</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>Ver <see cref="QuipuxOutcome"/>.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>
    /// Detalle en jsonb. SIEMPRE sanitizado: nunca request/response crudos. Es la regla más fuerte
    /// del repo — FLIT 1.0 guardaba <c>jsonSend</c>/<c>jsonResponse</c> completos, con PII del
    /// propietario dentro. Un string plano no es JSON válido y rompe el INSERT con 22P02, así que
    /// el escritor lo envuelve como escalar JSON.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>Correlaciona todos los eventos de un mismo ciclo del worker (y con <c>admin.ot_api_call_logs</c>).</summary>
    public Guid? CorrelationId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// Vocabulario cerrado de etapas. Cubre el camino completo de una radicación, de modo que la
/// bitácora de un trámite se lee como una secuencia legible sin cruzar tablas.
/// </summary>
public static class QuipuxStage
{
    /// <summary>El worker reclamó la submission (<c>FOR UPDATE SKIP LOCKED</c>).</summary>
    public const string Claimed = "claimed";

    /// <summary>Se aseguró el PDF consolidado maestro (generado o reutilizado por vigencia).</summary>
    public const string ConsolidadoGenerado = "consolidado_generado";

    /// <summary>El PDF quedó en el bucket de Quipux.</summary>
    public const string S3Subido = "s3_subido";

    public const string RegistroEnviado = "registro_enviado";

    public const string RegistroRespuesta = "registro_respuesta";

    public const string RegistroError = "registro_error";

    public const string ConsultaEnviada = "consulta_enviada";

    public const string ConsultaRespuesta = "consulta_respuesta";

    public const string ConsultaError = "consulta_error";

    /// <summary>Quipux aprobó; el trámite transicionó a <c>aprobado</c>.</summary>
    public const string Aprobado = "aprobado";

    /// <summary>Quipux rechazó; el trámite transicionó a <c>rechazado</c> con motivo.</summary>
    public const string Rechazado = "rechazado";

    /// <summary>Agotó los intentos. Se loguea además como Critical.</summary>
    public const string DeadLetter = "dead_letter";

    /// <summary>
    /// Un SuperAdmin re-encoló manualmente una radicación <c>fallido</c> desde la consola de cola
    /// (HU #10774). No lo produce ningún worker: es una acción de operación. Sin él, la reaparición
    /// de un trámite muerto en la cola sería un hueco inexplicable en el timeline del futuro LOG QX.
    /// El actor NO viaja en <c>detail</c> (regla PII del repo, ver <c>QuipuxSubmissionAuditLog</c>);
    /// queda en el log de aplicación.
    /// </summary>
    public const string ReintentoManual = "reintento_manual";

    /// <summary>
    /// Un SuperAdmin canceló manualmente una radicación <c>pendiente</c> antes de que se radicara
    /// (HU #10774). Lleva la submission a <c>fallido</c>. Igual que <see cref="ReintentoManual"/>,
    /// el actor no se persiste aquí.
    /// </summary>
    public const string CanceladoManual = "cancelado_manual";
}

/// <summary>Resultado de una etapa.</summary>
public static class QuipuxOutcome
{
    public const string Ok = "ok";

    /// <summary>Fallo reintentable (timeout, red, 5xx): la submission sigue reclamable.</summary>
    public const string ErrorTransitorio = "error_transitorio";

    /// <summary>Fallo definitivo: no tiene sentido reintentar con la misma entrada.</summary>
    public const string ErrorDefinitivo = "error_definitivo";

    /// <summary>La etapa no se ejecutó porque no hacía falta (p. ej. consolidado ya vigente).</summary>
    public const string Omitido = "omitido";
}
