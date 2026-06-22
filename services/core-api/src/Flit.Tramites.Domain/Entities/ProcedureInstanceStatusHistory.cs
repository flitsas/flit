namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureInstanceStatusHistory
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
    public string? Reason { get; set; }
    public string Metadata { get; set; } = "{}";

    public ProcedureInstance? ProcedureInstance { get; set; }
}
