using System.Text.Json;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Calendario;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Semillas;

/// <summary>
/// Datos de arranque, idempotentes: se agrega lo que falta y nunca se pisa lo que un administrador ya cambió.
/// Corren después de las migraciones, en cada arranque.
/// </summary>
public static class Semillas
{
    public const int PrimerAnio = 2015;
    public const int UltimoAnio = 2040;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task SembrarAsync(ComparendosDb db, TimeProvider time, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        var ahora = time.GetUtcNow();
        await CalendarioAsync(db, ct).ConfigureAwait(false);
        await OrganismosAsync(db, ahora, ct).ConfigureAwait(false);
        await TrabajosAsync(db, ahora, ct).ConfigureAwait(false);
        await TipologiasSemilla.SembrarAsync(db, ahora, ct).ConfigureAwait(false);
        await PromptOcrSemilla.SembrarAsync(db, ahora, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await PlantillasSemilla.SembrarAsync(db, ahora, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Días de 2015 a 2040 con los festivos calculados (Ley Emiliani y Pascua) [MOD03 RN-06, S-26].</summary>
    private static async Task CalendarioAsync(ComparendosDb db, CancellationToken ct)
    {
        if (await db.CalendarioDias.AnyAsync(ct).ConfigureAwait(false))
            return;

        var festivos = FestivosColombia.Entre(PrimerAnio, UltimoAnio).ToDictionary(f => f.Fecha, f => f.Nombre);
        for (var d = new DateOnly(PrimerAnio, 1, 1); d <= new DateOnly(UltimoAnio, 12, 31); d = d.AddDays(1))
        {
            var finDeSemana = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var festivo = festivos.TryGetValue(d, out var nombre);
            db.CalendarioDias.Add(new CalendarioDia
            {
                Fecha = d,
                EsFinDeSemana = finDeSemana,
                EsFestivo = festivo,
                DescripcionFestivo = festivo ? nombre : null,
                EsHabil = !finDeSemana && !festivo,
                Origen = "SEMILLA",
            });
        }
    }

    private sealed record OrganismoSemilla(Guid Id, string Codigo, string Nombre, string? MunicipioCodigo, string? Municipio, string? Departamento, bool Activo, List<string>? CorreosRadicacion);

    /// <summary>
    /// Organismos de tránsito del RUNT (los mismos ids y códigos que la plataforma) con sus correos de radicación
    /// importados de V1 (<c>traffic_secretaries.notifier_email</c>, ver context/gdc/v1/correos-ot-importados.md) [Excel J6].
    /// Un organismo que ya existe solo recibe correos si no tiene ninguno: lo que el SuperAdmin corrigió no se pisa.
    /// </summary>
    private static async Task OrganismosAsync(ComparendosDb db, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var stream = typeof(Semillas).Assembly.GetManifestResourceStream("Flit.Comparendos.Api.Semillas.organismos-transito.json")
            ?? throw new InvalidOperationException("Falta el recurso organismos-transito.json.");
        var semilla = await JsonSerializer.DeserializeAsync<List<OrganismoSemilla>>(stream, Json, ct).ConfigureAwait(false) ?? [];
        var existentes = await db.OrganismosTransito.ToDictionaryAsync(o => o.Codigo, ct).ConfigureAwait(false);
        foreach (var o in semilla.Where(o => existentes.ContainsKey(o.Codigo) && o.CorreosRadicacion is { Count: > 0 }))
        {
            if (existentes[o.Codigo].CorreosRadicacion.Count == 0)
                existentes[o.Codigo].CorreosRadicacion = [.. o.CorreosRadicacion!];
        }

        foreach (var o in semilla.Where(o => !existentes.ContainsKey(o.Codigo)))
        {
            db.OrganismosTransito.Add(new OrganismoTransito
            {
                Id = o.Id,
                Codigo = o.Codigo,
                Nombre = o.Nombre,
                CodigoDivipola = o.MunicipioCodigo,
                Municipio = o.Municipio,
                Departamento = o.Departamento,
                ActivoPlataforma = o.Activo,
                CorreosRadicacion = o.CorreosRadicacion ?? [],
                SincronizadoEn = ahora,
                CreadoEn = ahora,
            });
        }
    }

    /// <summary>Trabajos programados y horarios por defecto (§3.10, §3.11), configuración de alertas (§3.32).</summary>
    private static async Task TrabajosAsync(ComparendosDb db, DateTimeOffset ahora, CancellationToken ct)
    {
        (TipoTrabajo Tipo, string Cron, bool SoloHabiles)[] trabajos =
        [
            // La ingesta no tiene cron global: cada empresa tiene su agenda de consultas (HU #13521).
            (TipoTrabajo.AvisosConductor, "*/15 * * * *", true),
            (TipoTrabajo.ReintentoContraventor, "0 6 * * *", false),
            (TipoTrabajo.ReporteSemanal, "0 17 * * 5", false),
            (TipoTrabajo.ReporteMensual, "0 18 L * *", false),
        ];
        var existentes = await db.TrabajosProgramados.Select(t => t.Tipo).ToListAsync(ct).ConfigureAwait(false);
        foreach (var (tipo, cron, soloHabiles) in trabajos.Where(t => !existentes.Contains(t.Tipo)))
            db.TrabajosProgramados.Add(new TrabajoProgramado { Id = Guid.CreateVersion7(), Tipo = tipo, ExpresionCron = cron, SoloDiasHabiles = soloHabiles, CreadoEn = ahora });

        var avisos = await db.SchedulerAvisos.Select(s => s.TipoAviso).ToListAsync(ct).ConfigureAwait(false);
        foreach (var aviso in new[] { TipoAviso.Aviso1, TipoAviso.Aviso2, TipoAviso.Aviso3 }.Where(a => !avisos.Contains(a)))
            db.SchedulerAvisos.Add(new SchedulerAviso { TipoAviso = aviso, HoraEnvio = new TimeOnly(7, 0), CreadoEn = ahora });

        if (!await db.AlertaConfiguraciones.AnyAsync(ct).ConfigureAwait(false))
            db.AlertaConfiguraciones.Add(new AlertaConfiguracion { Id = 1, CreadoEn = ahora });
    }
}
