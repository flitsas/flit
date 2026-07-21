namespace Flit.Tramites.Domain.Integration;

/// <summary>Estado destino de la radicación según la ruta de preasignación de placa (Feature #10587).</summary>
public enum PlateRouteDecision
{
    /// <summary>Ruta estándar: el trámite se entrega al OT (entregado).</summary>
    Standard,

    /// <summary>Flujo A: la compañía eligió una placa disponible → el trámite queda asignado.</summary>
    Asignado,

    /// <summary>Flujo B: sin rango/placa → el trámite se envía al OT para que asigne (preasignado).</summary>
    Preasignado,
}

/// <summary>
/// Puerto que decide, al radicar, si el trámite sigue la ruta de preasignación de placa y a qué
/// estado aterriza (Feature #10587). Reserva la placa elegida cuando aplica. Desacopla trámites del
/// inventario de placas (mismo patrón que <see cref="IRnmcRequirementPolicy"/>).
/// </summary>
public interface IPlatePreassignPolicy
{
    /// <summary>
    /// Decide la ruta para un trámite de matrícula inicial con preasignación activa (flag de la
    /// compañía + grant + allow_plate_preassign del OT). Si hay placa elegida y disponible, la reserva
    /// (disponible→preasignada) y devuelve <see cref="PlateRouteDecision.Asignado"/>; sin placa/rango,
    /// <see cref="PlateRouteDecision.Preasignado"/>. En cualquier otro caso, <see cref="PlateRouteDecision.Standard"/>.
    /// </summary>
    Task<PlateRouteDecision> DecideAsync(
        Guid tenantId,
        Guid instanceId,
        CancellationToken cancellationToken = default);
}

/// <summary>Implementación inerte (ruta estándar siempre) — default seguro para tests.</summary>
public sealed class NullPlatePreassignPolicy : IPlatePreassignPolicy
{
    public static NullPlatePreassignPolicy Instance { get; } = new();

    public Task<PlateRouteDecision> DecideAsync(
        Guid tenantId,
        Guid instanceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PlateRouteDecision.Standard);
}
