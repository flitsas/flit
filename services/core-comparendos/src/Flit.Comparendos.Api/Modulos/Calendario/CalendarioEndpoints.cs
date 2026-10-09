using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Ingesta;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Calendario;

/// <summary>
/// Calendario hábil (§7.25, RN-075): el SuperAdmin agrega o quita un festivo manual con descripción (auditado). Antes
/// de guardar ve cuántos comparendos se recalcularán; al guardar se recalculan los activos con alguna fecha guardada
/// desde el día modificado, cada uno con su evento PLAZOS_CALCULADOS (fechas anteriores y nuevas).
/// </summary>
public static class CalendarioEndpoints
{
    public sealed record CambioDia(bool EsFestivo, string? Descripcion);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/admin/calendario").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        g.MapGet("", Anio);
        g.MapGet("/impacto", Impacto);
        g.MapPut("/{fecha}", Cambiar);
    }

    private static async Task<IResult> Anio(int? anio, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var a = anio ?? reloj.Hoy.Year;
        var (desde, hasta) = (new DateOnly(a, 1, 1), new DateOnly(a, 12, 31));
        var dias = await db.CalendarioDias.AsNoTracking().Where(d => d.Fecha >= desde && d.Fecha <= hasta).OrderBy(d => d.Fecha)
            .Select(d => new { d.Fecha, d.EsHabil, d.EsFinDeSemana, d.EsFestivo, d.DescripcionFestivo, d.Origen }).ToListAsync(ct).ConfigureAwait(false);
        if (dias.Count == 0)
            throw ReglaException.NoEncontrado("El año");
        return Results.Ok(new { anio = a, habiles = dias.Count(d => d.EsHabil), festivos = dias.Count(d => d.EsFestivo), dias });
    }

    /// <summary>Comparendos activos con alguna fecha legal guardada desde el día modificado.</summary>
    private static IQueryable<Comparendo> Afectados(ComparendosDb db, DateOnly fecha) =>
        db.Comparendos.Where(c => c.Activo && (c.FechaLimiteGestion >= fecha || c.FechaFinDescuento50 >= fecha || c.FechaFinDescuento25 >= fecha
            || c.FechaCaducidad >= fecha || c.FechaPrescripcion >= fecha));

    private static async Task<IResult> Impacto(DateOnly fecha, ComparendosDb db, CancellationToken ct) =>
        Results.Ok(new { fecha, comparendos = await Afectados(db, fecha).CountAsync(ct).ConfigureAwait(false) });

    private static async Task<IResult> Cambiar(DateOnly fecha, CambioDia cambio, ComparendosDb db, CalendarioService calendario, Auditoria auditoria, Timeline timeline,
        UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        var dia = await db.CalendarioDias.FirstOrDefaultAsync(d => d.Fecha == fecha, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El día");
        if (dia.EsFinDeSemana)
            throw new ReglaException("DIA_FIN_DE_SEMANA", "Sábados y domingos ya no son hábiles: no hace falta marcarlos como festivos.");
        var descripcion = cambio.Descripcion?.Trim();
        if (cambio.EsFestivo && (string.IsNullOrEmpty(descripcion) || descripcion.Length > 120))
            throw new ReglaException("DESCRIPCION_REQUERIDA", "El festivo necesita una descripción de hasta 120 caracteres.");
        if (dia.EsFestivo == cambio.EsFestivo && (!cambio.EsFestivo || dia.DescripcionFestivo == descripcion))
            return Results.Ok(new { fecha, recalculados = 0 });

        auditoria.Registrar(null, "calendario_dia", fecha.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), cambio.EsFestivo ? "MARCAR_FESTIVO" : "QUITAR_FESTIVO",
            Auditoria.Diferencias(("es_festivo", dia.EsFestivo, cambio.EsFestivo), ("descripcion_festivo", dia.DescripcionFestivo, cambio.EsFestivo ? descripcion : null)));
        var cambioHabilidad = dia.EsFestivo != cambio.EsFestivo;
        (dia.EsFestivo, dia.EsHabil, dia.DescripcionFestivo, dia.Origen) = (cambio.EsFestivo, !cambio.EsFestivo, cambio.EsFestivo ? descripcion : null, "MANUAL");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        calendario.Invalidar();
        if (!cambioHabilidad)
            return Results.Ok(new { fecha, recalculados = 0 });

        // Recálculo por lotes: cada lote se confirma solo, para no sostener una transacción larga.
        var motor = await calendario.MotorAsync(ct).ConfigureAwait(false);
        var actor = Actor.De(usuario);
        var motivo = cambio.EsFestivo ? $"se marcó el {Plantillas.Formato.Fecha(fecha)} como festivo ({descripcion})" : $"el {Plantillas.Formato.Fecha(fecha)} dejó de ser festivo";
        var recalculados = 0;
        var ultimo = Guid.Empty;
        while (true)
        {
            db.ChangeTracker.Clear();
            var lote = await Afectados(db, fecha).Where(c => c.Id.CompareTo(ultimo) > 0).OrderBy(c => c.Id).Take(500).ToListAsync(ct).ConfigureAwait(false);
            if (lote.Count == 0)
                break;
            foreach (var c in lote)
            {
                if (Plazos.Recalcular(c, motor, reloj.Ahora) is { } antes)
                {
                    timeline.Agregar(c, TipoEventoTimeline.PlazosCalculados, $"Se recalcularon los plazos porque {motivo}. {Reconciliador.DescribirPlazos(c)}", actor,
                        Reconciliador.PlazosDatos(antes, c));
                    recalculados++;
                }
            }

            ultimo = lote[^1].Id;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(new { fecha, recalculados });
    }
}
