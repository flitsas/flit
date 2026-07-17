namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Placa individual del inventario de preasignación — <c>admin.plate_range_details</c>
/// (Feature #10587). Estado del ciclo de vida en <c>state</c> (disponible/preasignada/utilizada/
/// bloqueada/revocada). RLS por <c>app.current_tenant_id</c>.
/// </summary>
public sealed class PlateRangeDetailEntity
{
    public Guid Id { get; set; }

    /// <summary>Rango al que pertenece la placa.</summary>
    public Guid PlateRangeId { get; set; }

    /// <summary>Compañía dueña (denormalizado para RLS/consulta directa).</summary>
    public Guid TenantId { get; set; }

    /// <summary>OT que asignó el rango.</summary>
    public Guid TransitOfficeId { get; set; }

    /// <summary>Placa completa (ej. "ABC123").</summary>
    public string Plate { get; set; } = string.Empty;

    /// <summary>Estado del ciclo de vida (<see cref="Flit.Admin.Domain.PlatePreassign.PlateState"/>).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Trámite que tomó la placa (NULL mientras esté disponible).</summary>
    public Guid? ProcedureInstanceId { get; set; }

    /// <summary>Instante en que se reservó (preasignada).</summary>
    public DateTimeOffset? ReservedAt { get; set; }

    /// <summary>Instante en que se utilizó (aprobada en RUNT).</summary>
    public DateTimeOffset? UsedAt { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
