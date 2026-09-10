namespace Flit.Infrastructure.Persistence.Entities.Catalogs;

/// <summary>Carrocería de vehículo — <c>catalogs.vehicle_bodyworks</c>.</summary>
public sealed class VehicleBodywork
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    /// <summary>Descripción. Valor efectivo en el FUR (<c>vehicle_body_type</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Clase RUNT. <c>null</c> = respaldo cuando la consulta no trae clase.</summary>
    public string? ClassVehicle { get; set; }

    public bool IsActive { get; set; } = true;

    public string ExternalRefs { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
