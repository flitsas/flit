using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// HU #11486 (ADR-0046) — proyecta instancia + field_values al modelo del correo de asignación de placa.
/// </summary>
public interface IPlateAssignmentEmailModelProjector
{
    PlateAssignmentEmailModelData Project(
        ProcedureInstance instance,
        IReadOnlyList<ProcedureInstanceActor> actors,
        IReadOnlyDictionary<string, string?> fieldValues);
}
