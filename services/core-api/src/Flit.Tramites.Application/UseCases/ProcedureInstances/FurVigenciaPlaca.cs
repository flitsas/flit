using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13055 — el FUR persistido es anterior a la última asignación/corrección de placa del OT, así
/// que no la refleja.
///
/// <para>Los consolidados solo fusionan el FUR ya persistido (Feature #11066). Si la regeneración del
/// FUR que dispara la asignación de placa falla (es best-effort) o la corrección de placa no la
/// dispara, cualquier consolidado reconstruido después sale con el FUR viejo, sin placa, y queda
/// marcado vigente hasta que alguien pulse "Limpiar consolidado". Con esta comprobación ambos
/// consolidados regeneran primero el FUR, sin depender de que el hito lo haya logrado.</para>
///
/// <para>Base de tiempo: <see cref="ProcedureInstance.PlateAssignedAt"/> y
/// <see cref="ProcedureInstance.PlateUpdatedAt"/> solo los escriben la asignación y la corrección de
/// placa, así que no hay falsos positivos por otras escrituras sobre la instancia.</para>
/// </summary>
internal static class FurVigenciaPlaca
{
    internal static bool FurDesactualizado(ProcedureInstance instance)
    {
        // La documentación de un trámite en estado final es la que el organismo tuvo a la vista.
        if (TramiteEstado.EsFinal(instance.Status))
            return false;

        var marcaPlaca = Max(instance.PlateAssignedAt, instance.PlateUpdatedAt);
        if (marcaPlaca is null)
            return false;

        var fur = instance.Attachments
            .Where(a => string.Equals(a.Tipo, "fur", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault();

        // Sin FUR lo resuelve la cascada de siempre. Un FUR cargado a mano (Source="user") nunca lo
        // reemplaza la generación: marcarlo desactualizado solo forzaría intentos inútiles en cada vista.
        if (fur is null || string.Equals(fur.Source, "user", StringComparison.OrdinalIgnoreCase))
            return false;

        return fur.UploadedAt < marcaPlaca;
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);
}
