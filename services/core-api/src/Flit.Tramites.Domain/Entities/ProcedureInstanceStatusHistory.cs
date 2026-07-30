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

    /// <summary>
    /// Feature #11076 (G3) — Rol del actor al momento del evento de estado.
    /// NULL indica que el registro es previo al backfill; el frontend muestra "Historial no disponible".
    /// </summary>
    public Guid? RoleIdAtTime { get; set; }

    /// <summary>
    /// Feature #11076 (G3) — ID de la OT o empresa del actor al momento del evento.
    /// Interpretar junto a <see cref="OrganizationTypeAtTime"/>.
    /// </summary>
    public Guid? OrganizationIdAtTime { get; set; }

    /// <summary>
    /// Feature #11076 (G3) — Tipo de organización del actor: 'ot' | 'empresa'.
    /// NULL si el registro no tiene datos de auditoría enriquecida.
    /// </summary>
    public string? OrganizationTypeAtTime { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
