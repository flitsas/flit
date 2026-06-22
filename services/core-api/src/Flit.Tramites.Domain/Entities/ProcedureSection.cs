namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureSection
{
    public Guid Id { get; set; }
    public Guid ProcedureStepId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public short SortOrder { get; set; }
    public string Layout { get; set; } = "single";
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ProcedureStep? ProcedureStep { get; set; }
    public ICollection<FormField> FormFields { get; set; } = [];
}
