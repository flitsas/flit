namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureSection
{
    public Guid Id { get; set; }
    public Guid ProcedureStepId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public short SortOrder { get; set; }
    public string Layout { get; set; } = "single";

    /// <summary>
    /// Tipo de renderer frontend para el SectionRendererRegistry (CFD-09).
    /// Valores: vehicle_query | document_checklist | actor_form | commercial |
    /// biometric | signature_fur | plate_request | prenda_decision | generic_form.
    /// </summary>
    public string SectionType { get; set; } = "generic_form";

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ProcedureStep? ProcedureStep { get; set; }
    public ICollection<FormField> FormFields { get; set; } = [];
}
