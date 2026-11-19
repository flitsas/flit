namespace Flit.Consultas.Api.Persistence;

/// <summary>
/// Contingencia de proveedor RUNT de un tipo de consulta (<c>consultas.runt_contingencies</c>, Épica #14644, HU #14647).
/// Global de plataforma, sin empresa. Una fila por tipo (<c>vehicle_vin</c>, <c>vehicle_plate</c>, <c>conductor</c>)
/// que alguna vez se tocó; <see cref="Provider"/> nulo = sin contingencia (la fila conserva el motivo del último cambio).
/// </summary>
public sealed class ContingenciaProveedorRunt
{
    public Guid Id { get; set; }

    /// <summary>Tipo de consulta: <c>vehicle_vin</c>, <c>vehicle_plate</c> o <c>conductor</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Proveedor forzado, o nulo si no hay contingencia.</summary>
    public string? Provider { get; set; }

    /// <summary>Motivo del último cambio (activar, cambiar o apagar).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Nombre visible de quien hizo el último cambio.</summary>
    public string UpdatedByName { get; set; } = string.Empty;

    /// <summary>Id del usuario que hizo el último cambio.</summary>
    public Guid? UpdatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
