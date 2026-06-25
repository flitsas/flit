namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureInstance
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureTypeId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = Enums.ProcedureInstanceStatus.Draft;

    // Rework trámites (Slice 1) — modalidad/tipología/checklist explícitos
    public string ModalidadEntrada { get; set; } = "matricula_inicial";
    public string? TipologiaCodigo { get; set; }
    public string ChecklistEstado { get; set; } = "{}";

    public Guid? TransitOfficeId { get; set; }

    /// <summary>
    /// Marca de "borrador finalizado" (HU #10349, fase 2). El gestor finaliza la captura de datos
    /// (actores, documentos, organismo) y el trámite queda en <c>draft</c> a la espera de la validación
    /// de identidad async del cliente. Cuando llega <c>IdentityValidationCompleted</c> (aprobado), el
    /// consumidor de outbox firma/encadena automáticamente los borradores finalizados del sujeto. Null
    /// mientras el borrador no se ha finalizado. NO equivale a radicar (Draft→Submitted sigue exigiendo
    /// identidad + FUR + gates en <see cref="UseCases.ProcedureInstances.SubmitGate"/>).
    /// </summary>
    public DateTimeOffset? DraftFinalizedAt { get; set; }

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
