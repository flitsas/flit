namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureInstanceActor
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public Guid ProcedureEntityId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
    public ProcedureEntity? ProcedureEntity { get; set; }
}
