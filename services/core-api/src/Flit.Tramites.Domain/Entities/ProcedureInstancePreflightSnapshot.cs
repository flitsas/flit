namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Semáforo preflight persistido (server-driven). Literal de overall: green | yellow | red
/// (DI-1: estandarizado en 'yellow', no 'amber'). Slice 1 — schema núcleo del rework de trámites.
/// </summary>
public sealed class ProcedureInstancePreflightSnapshot
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public string Overall { get; set; } = string.Empty;
    public string Checks { get; set; } = "{}";
    public string? Provider { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
