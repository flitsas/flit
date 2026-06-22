namespace Flit.Tramites.Domain.Entities;

public sealed class ConformationRule
{
    public Guid Id { get; set; }
    public Guid ProcedureTypeId { get; set; }
    public Guid ProcedureEntityId { get; set; }
    public bool IsActive { get; set; } = true;
    public short SortOrder { get; set; }
    public string ValidationProfile { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ProcedureType? ProcedureType { get; set; }
    public ProcedureEntity? ProcedureEntity { get; set; }
}
