using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Domain.Entities;

namespace Flit.Infrastructure.Notifications.Tramites;

/// <summary>Adaptador DI sobre <see cref="PlateAssignmentEmailModelProjector"/> (HU #11486).</summary>
internal sealed class PlateAssignmentEmailModelProjectorService : IPlateAssignmentEmailModelProjector
{
    public PlateAssignmentEmailModelData Project(
        ProcedureInstance instance,
        IReadOnlyList<ProcedureInstanceActor> actors,
        IReadOnlyDictionary<string, string?> fieldValues)
    {
        var model = PlateAssignmentEmailModelProjector.Project(instance, actors, fieldValues);
        return new PlateAssignmentEmailModelData(
            model.ClienteNombre,
            model.Placa,
            model.EstadoActual,
            model.Ciudad,
            model.SecretariaTransito);
    }
}
