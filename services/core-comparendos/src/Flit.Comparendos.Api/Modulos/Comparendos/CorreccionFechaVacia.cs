using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Comparendos;

/// <summary>
/// HU #13784 (hallazgo B16-1): los comparendos que entraron con la fecha de notificación 01-01-2000 antes de tratarla como
/// ausente la conservan, porque la ingesta no borra un dato que la fuente deja de traer. Al arrancar se les quita y se
/// recalculan sus plazos, con un evento en la línea de tiempo. Solo toca los que aún la tienen: no se repite.
/// </summary>
public static class CorreccionFechaVacia
{
    public static async Task<int> AplicarAsync(ComparendosDb db, CalendarioService calendario, Timeline timeline, Reloj reloj, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(calendario);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(reloj);
        var afectados = await db.Comparendos.Where(c => c.FechaNotificacion == Plazos.FechaVaciaDeLasFuentes).ToListAsync(ct).ConfigureAwait(false);
        if (afectados.Count == 0)
            return 0;

        var motor = await calendario.MotorAsync(ct).ConfigureAwait(false);
        foreach (var c in afectados)
        {
            c.FechaNotificacion = null;
            Plazos.Recalcular(c, motor, reloj.Ahora);
            timeline.Agregar(c, TipoEventoTimeline.PlazosCalculados,
                "Se quitó la fecha de notificación 01-01-2000 que traía la fuente (es su valor cuando no la tiene) y se recalcularon los plazos.",
                Actor.Sistema);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return afectados.Count;
    }
}
