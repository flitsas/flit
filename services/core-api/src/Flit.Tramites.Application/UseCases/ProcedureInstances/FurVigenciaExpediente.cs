using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13055 — el FUR persistido es anterior al último cambio del expediente que imprime, así que
/// no lo refleja.
///
/// <para>Los consolidados solo fusionan el FUR ya persistido (Feature #11066). Antes, un cambio de
/// datos desde cualquier pantalla (gestor, OT o administración) —o la placa que asigna o corrige el
/// OT, cuya regeneración del FUR es best-effort— dejaba el FUR viejo dentro de cualquier consolidado
/// reconstruido después, marcado vigente hasta que alguien pulsaba «Limpiar consolidado». Con esta
/// comprobación el wizard, el maestro y el worker de regeneración anticipada regeneran primero el FUR,
/// sin depender de que cada hito se acuerde de hacerlo.</para>
///
/// <para>Marcas: <see cref="ProcedureInstance.ExpedienteActualizadoEn"/> (la sella
/// <c>ConsolidadoVigenciaTracker</c> ante cualquier cambio de datos del FUR),
/// <see cref="ProcedureInstance.PlateAssignedAt"/> y <see cref="ProcedureInstance.PlateUpdatedAt"/>
/// (la placa es una columna de la instancia que no pasa por las filas hijas del expediente).</para>
/// </summary>
public static class FurVigenciaExpediente
{
    /// <summary>Tipo del adjunto del FUR.</summary>
    public const string TipoFur = "fur";

    public static bool FurDesactualizado(ProcedureInstance instance)
    {
        // La documentación de un trámite en estado final es la que el organismo tuvo a la vista.
        if (TramiteEstado.EsFinal(instance.Status))
            return false;

        var marca = Max(Max(instance.PlateAssignedAt, instance.PlateUpdatedAt), instance.ExpedienteActualizadoEn);
        if (marca is null)
            return false;

        var fur = instance.Attachments
            .Where(a => string.Equals(a.Tipo, TipoFur, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault();

        // Sin FUR lo resuelve la cascada de siempre. Un FUR cargado a mano (Source="user") nunca lo
        // reemplaza la generación: marcarlo desactualizado solo forzaría intentos inútiles en cada vista.
        if (fur is null || string.Equals(fur.Source, "user", StringComparison.OrdinalIgnoreCase))
            return false;

        return fur.UploadedAt < marca;
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);
}
