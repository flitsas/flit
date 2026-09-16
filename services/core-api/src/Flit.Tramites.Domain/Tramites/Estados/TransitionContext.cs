using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Contexto que necesita <see cref="TramiteTransitionPolicy"/> para decidir las aristas que la
/// máquina estructural no puede decidir sola (ADR-0059).
/// </summary>
/// <param name="RequiresPlateRequest">
/// <c>gate_profile.requiresPlateRequest</c> del tipo de trámite: ¿este tipo pide placa? Es la ÚNICA
/// puerta de entrada a <see cref="TramiteEstado.Preasignacion"/> / <see cref="TramiteEstado.Asignado"/>.
/// </param>
/// <param name="HasPlate">¿El trámite ya tiene placa (RUNT, digitada o asignada por el OT)?</param>
/// <param name="Actor">Quién pide la transición.</param>
/// <param name="SubsanacionActiva">
/// Flag <c>subsanacion_activa</c> sobre <see cref="TramiteEstado.Rechazado"/> (ADR-0033): sin él no se
/// re-radica.
/// </param>
public sealed record TransitionContext(
    bool RequiresPlateRequest,
    bool HasPlate,
    TramiteActor Actor,
    bool SubsanacionActiva = false)
{
    /// <summary>
    /// Construye el contexto desde la instancia cargada con su tipo y sus <c>field_values</c>. La placa
    /// se lee de <c>field_values.plate</c> (fuente de verdad) y, si el grafo no la trae, de la columna
    /// denormalizada <see cref="ProcedureInstance.Plate"/>.
    /// </summary>
    public static TransitionContext ForInstance(ProcedureInstance instance, TramiteActor actor)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var plate = instance.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, VehicleFieldKeys.Plate, StringComparison.OrdinalIgnoreCase))
            ?.ValueText
            ?? instance.Plate;

        return new TransitionContext(
            ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile).RequiresPlateRequest,
            !string.IsNullOrWhiteSpace(plate),
            actor,
            instance.SubsanacionActiva);
    }
}
