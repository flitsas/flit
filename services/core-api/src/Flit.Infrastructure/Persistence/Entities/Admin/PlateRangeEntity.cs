namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Rango de placas de preasignación asignado por un OT a una compañía — <c>admin.plate_ranges</c>
/// (Feature #10587). Se explota en <see cref="PlateRangeDetailEntity"/> (una fila por placa).
/// RLS por <c>app.current_tenant_id</c> (tenant = compañía dueña del rango).
/// </summary>
public sealed class PlateRangeEntity
{
    public Guid Id { get; set; }

    /// <summary>Compañía dueña del rango.</summary>
    public Guid TenantId { get; set; }

    /// <summary>OT que asignó el rango.</summary>
    public Guid TransitOfficeId { get; set; }

    /// <summary>Prefijo de 3 letras (ej. "ABC").</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Número inicial del rango (000–999).</summary>
    public int RangeFrom { get; set; }

    /// <summary>Número final del rango (000–999).</summary>
    public int RangeTo { get; set; }

    /// <summary>Instante hasta el cual el rango es editable (created_at + 60 min).</summary>
    public DateTimeOffset EditableUntil { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
