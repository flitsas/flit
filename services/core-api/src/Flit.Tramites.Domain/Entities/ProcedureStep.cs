namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureStep
{
    public Guid Id { get; set; }
    public Guid ProcedureTypeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public short SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ProcedureType? ProcedureType { get; set; }
    public ICollection<ProcedureSection> Sections { get; set; } = [];
}
