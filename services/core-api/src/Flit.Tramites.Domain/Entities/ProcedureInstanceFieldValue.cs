namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureInstanceFieldValue
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public Guid? FormFieldId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string? ValueText { get; set; }
    public string? ValueJson { get; set; }
    public string Source { get; set; } = "user";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
    public FormField? FormField { get; set; }
}
