namespace Flit.Modules.Quipux.Domain.Envios;

/// <summary>
/// Radicación de un trámite en Quipux (<c>tramites.quipux_submissions</c>). Es el registro de
/// estado que reemplaza al par <c>quipux_integration_log</c>/<c>quipux_integration_states</c> de
/// FLIT 1.0, donde el estado vivía implícito en el último log y provocaba trámites huérfanos.
/// </summary>
public sealed class QuipuxSubmission
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProcedureInstanceId { get; set; }

    /// <summary>
    /// El <c>documento</c> que se envía a Quipux: <c>{empresa}_{prefijo}_{yyyyMMdd_HHmm}_{sufijo}</c>.
    /// Se calcula UNA sola vez al encolar y NO se recalcula en los reintentos — es lo que hace
    /// posible consultar el estado de un registro cuyo POST expiró (Quipux pudo haberlo radicado).
    /// En FLIT 1.0 se regeneraba en cada intento con precisión de minuto, así que un reintento
    /// producía un nombre distinto y podía duplicar el trámite en Quipux.
    /// </summary>
    public string DocumentName { get; set; } = string.Empty;

    /// <summary>Adjunto <c>consolidado_maestro</c> a subir al bucket de Quipux (el <c>documentoFlit</c> de 1.0).</summary>
    public Guid AttachmentId { get; set; }

    /// <summary>Código de trámite Quipux (16 traspaso bilateral, 213 unilateral, 13 matrícula). Resuelto desde <c>procedure_types.external_refs</c>, nunca hardcodeado.</summary>
    public int QuipuxProcedureType { get; set; }

    /// <summary>Código de requisito Quipux (51 en todos los flujos conocidos). Desde <c>external_refs</c>.</summary>
    public int QuipuxRequirementType { get; set; }

    /// <summary>Código DIVIPO del organismo, copiado de <c>catalogs.transit_offices.divipo_code</c> al encolar.</summary>
    public string DivipoCode { get; set; } = string.Empty;

    /// <summary>Ver <see cref="QuipuxSubmissionEstado"/>.</summary>
    public string Status { get; set; } = QuipuxSubmissionEstado.Pendiente;

    /// <summary>Último <c>codigo</c> del registro (81 = radicado; 0 = error, en 1.0 el gancho del job de timeout).</summary>
    public int? QxRegisterCode { get; set; }

    /// <summary>Último <c>estadoTramite.codigo</c> de la consulta (2 aprobado, 3 rechazado).</summary>
    public int? QxProcedureCode { get; set; }

    /// <summary>Descripción del rechazo de Quipux. Equivale al <c>rejectionObservation</c> de 1.0; se copia al <c>reason</c> del historial del trámite.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Intentos de radicación. Se incrementa ANTES de procesar: si el proceso muere, el intento igual cuenta.</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Momento en que un worker tomó la submission para radicarla (lease). <c>FOR UPDATE SKIP
    /// LOCKED</c> solo hace exclusivo el INSTANTE del reclamo: en cuanto la transacción del claim
    /// commitea —y debe commitear antes del POST, que es lento— la fila vuelve a ser reclamable.
    /// Sin este sello, en la ventana de redeploy (el CD hace <c>docker compose up -d</c> sin
    /// <c>--wait</c>, así que el contenedor viejo y el nuevo se solapan) dos workers podrían
    /// radicar el mismo documento en la secretaría: exactamente el defecto de FLIT 1.0 que este
    /// diseño existe para evitar. El claim exige que el lease esté vacío o vencido.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    public DateTimeOffset? RegisteredAt { get; set; }

    public DateTimeOffset? LastPolledAt { get; set; }

    /// <summary>Consultas de estado realizadas. Acota el sondeo indefinido de un trámite que Quipux nunca resuelve.</summary>
    public int PollCount { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>¿Ya se radicó y solo falta que Quipux resuelva?</summary>
    public bool EstaRegistrada => string.Equals(Status, QuipuxSubmissionEstado.Registrado, StringComparison.Ordinal);

    /// <summary>
    /// ¿Hubo un intento de registro previo que falló? Si es así, el registrador debe CONSULTAR
    /// antes de reintentar: Quipux pudo haber radicado el documento aunque la respuesta HTTP
    /// expirara. Absorbe el job <c>validateStatusDocumentForTimeOut</c> de FLIT 1.0.
    /// </summary>
    public bool RequiereConsultaPrevia => Attempts > 0 && string.Equals(Status, QuipuxSubmissionEstado.Pendiente, StringComparison.Ordinal);
}
