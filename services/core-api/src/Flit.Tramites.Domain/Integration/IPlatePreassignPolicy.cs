namespace Flit.Tramites.Domain.Integration;

/// <summary>Estado destino de la radicación según la ruta de preasignación de placa (Feature #10587).</summary>
public enum PlateRouteDecision
{
    /// <summary>Ruta estándar: el trámite se entrega al OT (entregado).</summary>
    Standard,

    /// <summary>Flujo A: la compañía eligió una placa disponible → el trámite queda asignado.</summary>
    Asignado,

    /// <summary>
    /// Flujo A con parámetro compañía <c>plate_flow_skip_to_terminado</c>: placa reservada y se omite
    /// el paso del gestor (checks) → aterriza en terminado.
    /// </summary>
    Terminado,

    /// <summary>Flujo B: sin rango/placa → el trámite se envía al OT para que asigne (preasignado).</summary>
    Preasignado,

    /// <summary>
    /// HU #10806 — la compañía tiene la preasignación activa pero el OT está mal configurado
    /// (grant/allow ausente): la radicación se BLOQUEA en vez de degradar a estándar en silencio.
    /// </summary>
    Blocked,
}

/// <summary>Motivo de la decisión de ruta (HU #10806) — para trazabilidad y para gatear el submit.</summary>
public enum PlateRouteReason
{
    /// <summary>No es matrícula inicial.</summary>
    NotMatriculaInicial,

    /// <summary>Falta el <c>transit_office_id</c> o es inválido.</summary>
    NoOffice,

    /// <summary>La compañía no usa preasignación (flag off): ruta estándar sin fricción.</summary>
    PreassignNotEnabled,

    /// <summary>Compañía con preasignación activa pero OT mal configurado: bloquea la radicación.</summary>
    PreassignMisconfigured,

    /// <summary>Placa elegida y reservada (Flujo A) → asignado.</summary>
    PlateReserved,

    /// <summary>Placa reservada y la compañía omite el paso gestor → terminado.</summary>
    PlateReservedSkipToTerminado,

    /// <summary>
    /// Placa ya presente desde consulta RUNT (<c>source=consultation</c>): Flujo A sin exigir
    /// reserva de inventario → asignado.
    /// </summary>
    PlateFromConsultation,

    /// <summary>Placa RUNT + compañía omite el paso gestor → terminado.</summary>
    PlateFromConsultationSkipToTerminado,

    /// <summary>Sin placa (o reserva fallida de placa de usuario): se envía al OT para que asigne (Flujo B).</summary>
    NoPlate,
}

/// <summary>Resultado de la decisión de ruta con su motivo (HU #10806).</summary>
public sealed record PlateRouteResult(PlateRouteDecision Decision, PlateRouteReason Reason)
{
    public static readonly PlateRouteResult NotMatricula = new(PlateRouteDecision.Standard, PlateRouteReason.NotMatriculaInicial);
    public static readonly PlateRouteResult NoOffice = new(PlateRouteDecision.Standard, PlateRouteReason.NoOffice);
    public static readonly PlateRouteResult NotEnabled = new(PlateRouteDecision.Standard, PlateRouteReason.PreassignNotEnabled);
    public static readonly PlateRouteResult Misconfigured = new(PlateRouteDecision.Blocked, PlateRouteReason.PreassignMisconfigured);
    public static readonly PlateRouteResult Reserved = new(PlateRouteDecision.Asignado, PlateRouteReason.PlateReserved);
    public static readonly PlateRouteResult ReservedSkipToTerminado =
        new(PlateRouteDecision.Terminado, PlateRouteReason.PlateReservedSkipToTerminado);
    public static readonly PlateRouteResult FromConsultation =
        new(PlateRouteDecision.Asignado, PlateRouteReason.PlateFromConsultation);
    public static readonly PlateRouteResult FromConsultationSkipToTerminado =
        new(PlateRouteDecision.Terminado, PlateRouteReason.PlateFromConsultationSkipToTerminado);
    public static readonly PlateRouteResult NoPlate = new(PlateRouteDecision.Preasignado, PlateRouteReason.NoPlate);
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
    /// <see cref="PlateRouteDecision.Preasignado"/>. Si la compañía tiene la ruta activa pero el OT está
    /// mal configurado, <see cref="PlateRouteDecision.Blocked"/>. En otro caso, <see cref="PlateRouteDecision.Standard"/>.
    /// Devuelve además el <see cref="PlateRouteReason"/> para trazabilidad (HU #10806).
    /// </summary>
    Task<PlateRouteResult> DecideAsync(
        Guid tenantId,
        Guid instanceId,
        CancellationToken cancellationToken = default);
}

/// <summary>Implementación inerte (ruta estándar siempre) — default seguro para tests.</summary>
public sealed class NullPlatePreassignPolicy : IPlatePreassignPolicy
{
    public static NullPlatePreassignPolicy Instance { get; } = new();

    public Task<PlateRouteResult> DecideAsync(
        Guid tenantId,
        Guid instanceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PlateRouteResult.NotEnabled);
}
