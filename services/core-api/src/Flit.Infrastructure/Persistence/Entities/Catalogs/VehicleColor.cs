namespace Flit.Infrastructure.Persistence.Entities.Catalogs;

/// <summary>Color de vehículo — <c>catalogs.vehicle_colors</c> (catálogo RUNT / transformaciones FUR).</summary>
public sealed class VehicleColor
{
    public Guid Id { get; set; }

    /// <summary>Código del color (<c>color_code</c> del catálogo fuente).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Descripción del color (<c>color_description</c>). Valor efectivo en el FUR.</summary>
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>JSON de referencias externas (p.ej. <c>source_id</c>).</summary>
    public string ExternalRefs { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
