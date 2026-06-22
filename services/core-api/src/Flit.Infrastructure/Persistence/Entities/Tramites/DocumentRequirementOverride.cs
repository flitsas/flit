namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Override de obligatoriedad documental por Organismo de Tránsito —
/// <c>tramites.document_requirement_overrides</c> (DDL HU #10198). Personaliza, a nivel de
/// OT, si un documento del trámite es obligatorio (<c>REQUIRED</c>), opcional
/// (<c>OPTIONAL</c>) o no aplica (<c>NOT_APPLICABLE</c> → se oculta de la matriz de ese OT).
/// La ausencia de fila significa "hereda el default del trámite". Granular solo para OT.
/// </summary>
public sealed class DocumentRequirementOverride
{
    public Guid Id { get; set; }

    public Guid ProcedureTypeId { get; set; }

    public Guid DocumentTypeId { get; set; }

    /// <summary>Organismo de tránsito al que aplica el override.</summary>
    public Guid TransitOfficeId { get; set; }

    /// <summary><c>REQUIRED</c> | <c>OPTIONAL</c> | <c>NOT_APPLICABLE</c>.</summary>
    public string RequirementState { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
