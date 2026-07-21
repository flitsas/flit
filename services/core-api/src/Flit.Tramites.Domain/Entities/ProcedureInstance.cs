namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureInstance
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureTypeId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = Tramites.Estados.TramiteEstado.Borrador;

    // Rework trámites (Slice 1) — modalidad/tipología/checklist explícitos
    public string ModalidadEntrada { get; set; } = "matricula_inicial";
    public string? TipologiaCodigo { get; set; }
    public string ChecklistEstado { get; set; } = "{}";

    public Guid? TransitOfficeId { get; set; }

    /// <summary>
    /// Feature #10587 / HU #10785 — sub-estado INTERNO del flujo de asignación de placa, ortogonal al
    /// <see cref="Status"/> global (que permanece en <c>entregado</c> durante todo el sub-flujo). Valores:
    /// <c>null</c> (trámite sin ruta de placa, comportamiento estándar), <c>preasignado</c> (entregado al
    /// OT, esperando placa) y <c>asignado</c> (placa registrada; pendiente de SOAT + recepción del OT).
    /// Ver <see cref="Tramites.Estados.PlateFlowStatus"/>. Columna agregada por migración SQL cruda
    /// (la tabla está ExcludeFromMigrations); aquí solo se mapea al modelo EF.
    /// </summary>
    public string? PlateFlowStatus { get; set; }

    /// <summary>
    /// Marca de "borrador finalizado" (HU #10349, fase 2). El gestor finaliza la captura de datos
    /// (actores, documentos, organismo) y el trámite queda en <c>draft</c> a la espera de la validación
    /// de identidad async del cliente. Cuando llega <c>IdentityValidationCompleted</c> (aprobado), el
    /// consumidor de outbox firma/encadena automáticamente los borradores finalizados del sujeto. Null
    /// mientras el borrador no se ha finalizado. NO equivale a radicar (Draft→Submitted sigue exigiendo
    /// identidad + FUR + gates en <see cref="UseCases.ProcedureInstances.SubmitGate"/>).
    /// </summary>
    public DateTimeOffset? DraftFinalizedAt { get; set; }

    /// <summary>
    /// HU #10536 — el gestor marca el trámite como prioritario para que el OT lo revise con primacía.
    /// Solo afecta el ordenamiento de los listados (operación y bandeja del OT); NO altera el ciclo de
    /// vida ni los gates. Default false. Columna agregada por migración SQL cruda (tabla ExcludeFromMigrations).
    /// </summary>
    public bool Prioritario { get; set; }

    /// <summary>
    /// Feature #10701 / HU #10706 — marca de vigencia del expediente consolidado maestro. En
    /// <c>true</c> el <c>consolidado_maestro</c> persistido refleja el estado actual del expediente:
    /// el botón único "Ver consolidado" lo muestra tal cual (sin regenerar). Cualquier cambio
    /// importante —transición de estado (aprobar/rechazar/…) o adjuntar la Licencia de Tránsito—
    /// la baja a <c>false</c>, y la siguiente petición regenera el PDF antes de mostrarlo. Default
    /// false (un trámite sin consolidado vigente se genera al primer clic). Columna agregada por
    /// migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea al modelo EF.
    /// </summary>
    public bool ConsolidadoMaestroVigente { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? RulesSnapshotAt { get; set; }
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public ProcedureType? ProcedureType { get; set; }
    public ICollection<ProcedureInstanceActor> Actors { get; set; } = [];
    public ICollection<ProcedureInstanceFieldValue> FieldValues { get; set; } = [];
    public ICollection<ProcedureInstanceStatusHistory> StatusHistory { get; set; } = [];
    public ICollection<ProcedureInstanceAttachment> Attachments { get; set; } = [];
    public ICollection<ProcedureInstancePreflightSnapshot> PreflightSnapshots { get; set; } = [];
    public ICollection<ProcedureInstanceEvent> Events { get; set; } = [];
    public ICollection<ProcedureInstanceBiometricValidation> BiometricValidations { get; set; } = [];
    public ICollection<ProcedureInstanceSignature> Signatures { get; set; } = [];
    public ICollection<ProcedureInstanceParticipant> Participants { get; set; } = [];
    public ProcedureInstanceCommercial? Commercial { get; set; }
}
