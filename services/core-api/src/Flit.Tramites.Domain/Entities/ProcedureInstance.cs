namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureInstance
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureTypeId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = Enums.ProcedureInstanceStatus.Draft;
    public Guid? TransitOfficeId { get; set; }
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
}
