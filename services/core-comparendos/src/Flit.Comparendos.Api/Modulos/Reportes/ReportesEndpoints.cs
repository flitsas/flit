using Flit.Comparendos.Api.Infraestructura.Excel;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;
using Cell = Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;

namespace Flit.Comparendos.Api.Modulos.Reportes;

/// <summary>Dashboard ejecutivo (§7.8) y reporte de productividad (§7.9). Solo con <c>has_reportes_module</c>; el SuperAdmin ve una empresa o todas.</summary>
public static class ReportesEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/reportes");
        g.MapGet("/dashboard", Dashboard).RequirePermiso(Permisos.ReportesVer);
        g.MapGet("/radar", Radar).RequirePermiso(Permisos.ReportesVer);
        g.MapGet("/nits", Nits).RequirePermiso(Permisos.ReportesVer);
        g.MapGet("/productividad", Productividad).RequirePermiso(Permisos.ProductividadVer);
        g.MapGet("/productividad/detalle", Detalle).RequirePermiso(Permisos.ProductividadVer);
        g.MapGet("/productividad/excel", Excel).RequirePermiso(Permisos.ProductividadVer);
    }

    private static async Task<Alcance> AlcanceAsync(UsuarioActual usuario, Capacidades capacidades, Guid? empresa, Guid? nit, DateOnly? desde, DateOnly? hasta, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa, obligatoria: false);
        if (tenant is { } t && !usuario.EsSuperAdmin)
            await capacidades.ExigirAsync(t, Capacidad.Reportes, ct).ConfigureAwait(false);
        var h = hasta ?? reloj.Hoy;
        var d = desde ?? new DateOnly(h.Year, h.Month, 1);
        if (d > h)
            throw new ReglaException("RANGO_INVALIDO", "La fecha inicial no puede ser posterior a la final.");
        if (h.DayNumber - d.DayNumber > 3 * 366)
            throw new ReglaException("RANGO_INVALIDO", "El rango máximo es de tres años.");
        return new Alcance(tenant, nit, d, h);
    }

    private static async Task<IResult> Dashboard(Guid? empresa, Guid? nit, DateOnly? desde, DateOnly? hasta, UsuarioActual usuario, Capacidades capacidades, ServicioReportes reportes, Reloj reloj, CancellationToken ct)
    {
        var a = await AlcanceAsync(usuario, capacidades, empresa, nit, desde, hasta, reloj, ct).ConfigureAwait(false);
        var serie = await reportes.SerieAsync(a, ct).ConfigureAwait(false);
        var hoy = reloj.Hoy;
        string Mes(int anio, int mes) => $"{anio:0000}-{mes:00}";
        return Results.Ok(new
        {
            alcance = new { empresa = a.Tenant, a.Nit, a.Desde, a.Hasta },
            kpis = await reportes.KpisAsync(a, ct).ConfigureAwait(false),
            mensual = serie.TakeLast(12),
            anual = new
            {
                actual = Enumerable.Range(1, 12).Select(m => serie.FirstOrDefault(p => p.Periodo == Mes(hoy.Year, m))),
                anterior = Enumerable.Range(1, 12).Select(m => serie.FirstOrDefault(p => p.Periodo == Mes(hoy.Year - 1, m))),
            },
            porNit = await reportes.TotalesPorNitAsync(a, ct).ConfigureAwait(false),
        });
    }

    /// <summary>NITs monitoreados para el filtro del dashboard (sin exigir el permiso de monitoreo).</summary>
    private static async Task<IResult> Nits(Guid? empresa, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var a = await AlcanceAsync(usuario, capacidades, empresa, null, null, null, reloj, ct).ConfigureAwait(false);
        return Results.Ok(await db.IdentificacionesMonitoreadas.AsNoTracking().Where(i => a.Tenant == null || i.TenantId == a.Tenant)
            .OrderBy(i => i.NombreTitular).Select(i => new { i.Id, i.TenantId, i.TipoDocumento, i.NumeroBase, i.DigitoVerificacion, i.NombreTitular, i.Activo })
            .Take(1000).ToListAsync(ct).ConfigureAwait(false));
    }

    /// <summary>La lista detrás del KPI «Radar de vencimientos» (la pantalla también puede saltar a la bandeja con el filtro rápido).</summary>
    private static async Task<IResult> Radar(Guid? empresa, Guid? nit, UsuarioActual usuario, Capacidades capacidades, ServicioReportes reportes, Reloj reloj, CancellationToken ct)
    {
        var a = await AlcanceAsync(usuario, capacidades, empresa, nit, null, null, reloj, ct).ConfigureAwait(false);
        var limite = await reportes.LimiteRadarAsync(ct).ConfigureAwait(false);
        return Results.Ok(await reportes.Radar(a, reloj.Hoy, limite).OrderBy(c => c.FechaLimiteGestion).Take(500)
            .Select(c => new { c.Id, c.TenantId, c.Numero, c.Placa, c.ValorTotal, c.FechaLimiteGestion, c.SecretariaTexto }).ToListAsync(ct).ConfigureAwait(false));
    }

    private static async Task<IResult> Productividad(Guid? empresa, DateOnly? desde, DateOnly? hasta, UsuarioActual usuario, Capacidades capacidades, ServicioReportes reportes, Reloj reloj, CancellationToken ct)
    {
        var a = await AlcanceAsync(usuario, capacidades, empresa, null, desde, hasta, reloj, ct).ConfigureAwait(false);
        return Results.Ok(new { alcance = new { empresa = a.Tenant, a.Desde, a.Hasta }, filas = await reportes.ProductividadAsync(a, ct).ConfigureAwait(false) });
    }

    private static async Task<IResult> Detalle(Guid usuarioId, string metrica, Guid? empresa, DateOnly? desde, DateOnly? hasta, UsuarioActual usuario, Capacidades capacidades,
        ServicioReportes reportes, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var a = await AlcanceAsync(usuario, capacidades, empresa, null, desde, hasta, reloj, ct).ConfigureAwait(false);
        var ids = reportes.ComparendosDeMetrica(a, usuarioId, metrica).Distinct();
        return Results.Ok(await db.Comparendos.AsNoTracking().Where(c => ids.Contains(c.Id)).OrderByDescending(c => c.UltimoEventoEn).Take(1000)
            .Select(c => new { c.Id, c.TenantId, c.Numero, c.Placa, c.ValorTotal, c.FechaImposicion, c.Pagado, c.TieneDp }).ToListAsync(ct).ConfigureAwait(false));
    }

    private static async Task<IResult> Excel(Guid? empresa, DateOnly? desde, DateOnly? hasta, UsuarioActual usuario, Capacidades capacidades, ServicioReportes reportes, Reloj reloj, CancellationToken ct)
    {
        var a = await AlcanceAsync(usuario, capacidades, empresa, null, desde, hasta, reloj, ct).ConfigureAwait(false);
        var filas = await reportes.ProductividadAsync(a, ct).ConfigureAwait(false);
        var hoja = new TabularWorkbookWriter.Sheet("Productividad",
            [new("Usuario", 32), new("Comparendos gestionados", 14), new("DPs enviados", 12), new("Contraventores asignados", 14), new("Pagos registrados", 12),
             new("Avisos manuales", 12), new("Registrados por OCR", 12), new("Respuestas de OT", 12)],
            filas.Select(f => (IReadOnlyList<Cell>)[Cell.Of(f.Usuario), Cell.Of(f.Gestionados), Cell.Of(f.DpEnviados), Cell.Of(f.ContraventoresAsignados), Cell.Of(f.Pagos),
                Cell.Of(f.AvisosManuales), Cell.Of(f.OcrRegistrados), Cell.Of(f.RespuestasOt)]).ToList());
        return Results.File(TabularWorkbookWriter.Write([hoja]), TabularWorkbookWriter.ContentType, $"productividad-{a.Desde:yyyyMMdd}-{a.Hasta:yyyyMMdd}.xlsx");
    }
}
