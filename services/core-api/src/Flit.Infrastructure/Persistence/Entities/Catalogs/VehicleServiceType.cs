namespace Flit.Infrastructure.Persistence.Entities.Catalogs;

/// <summary>
/// Tipo de servicio del vehículo — <c>catalogs.vehicle_service_types</c>. Catálogo global cerrado
/// (sección 18 del FUR): particular, público, diplomático, oficial, especial, otros.
/// </summary>
public sealed class VehicleServiceType
{
    public Guid Id { get; set; }

    /// <summary>Código estable, contrato con <c>FurFieldMapper.MarkServicio</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Nombre visible del tipo de servicio (p.ej. "Particular").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Orden normativo 1-6 de las casillas de la sección 18 del FUR.</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>JSON de referencias externas (reservado; catálogo cerrado sin fuente externa hoy).</summary>
    public string ExternalRefs { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
