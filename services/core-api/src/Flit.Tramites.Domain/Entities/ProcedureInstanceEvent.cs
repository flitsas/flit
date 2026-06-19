namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Bitácora append-only de la instancia (timeline + QR). Slice 1 — schema núcleo del rework de trámites.
/// </summary>
public sealed class ProcedureInstanceEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
