namespace Flit.Tramites.Domain.Entities;

public sealed class FormField
{
    public Guid Id { get; set; }
    public Guid ProcedureSectionId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public short SortOrder { get; set; }
    public string? Options { get; set; }
    public string ValidationSchema { get; set; } = "{}";
    public string? DefaultValue { get; set; }
    public bool IsLocked { get; set; }
    public string? LockReason { get; set; }
    public Guid? ConsultationTemplateId { get; set; }
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ProcedureSection? ProcedureSection { get; set; }
    public ConsultationTemplate? ConsultationTemplate { get; set; }
}
