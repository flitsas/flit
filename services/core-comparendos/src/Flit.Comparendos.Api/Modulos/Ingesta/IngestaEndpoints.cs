using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Calendario;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ingesta;

/// <summary>Trabajos programados, ejecución manual y ciclos de ingesta (HU #13460). Solo SuperAdmin.</summary>
public static class IngestaEndpoints
{
    public sealed record TrabajoDto(string Tipo, string ExpresionCron, string? Siguiente, bool Activo, bool SoloDiasHabiles, DateTimeOffset? UltimaEjecucionEn, string? UltimoResultado);

    public sealed record CambioTrabajo(string? ExpresionCron, bool? Activo, bool? SoloDiasHabiles);

    public sealed record EjecucionManual(string Canal, Guid EmpresaId);

    public sealed record CicloDto(Guid Id, string Canal, string Disparo, Guid? EmpresaManual, DateTimeOffset InicioEn, DateTimeOffset? FinEn, int ClavesTotal, int ClavesError, string Resultado, string? MensajeError);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/admin/ingesta").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        g.MapGet("/trabajos", ListarTrabajos);
        g.MapPatch("/trabajos/{tipo}", CambiarTrabajo);
        g.MapPost("/ejecutar", Ejecutar);
        g.MapPost("/trabajos/{tipo}/ejecutar", EjecutarTrabajo);
        g.MapGet("/ciclos", ListarCiclos);
        g.MapGet("/ciclos/{id:guid}", DetalleCiclo);
        g.MapGet("/consultas/{id:guid}/respuesta", RespuestaCruda);
    }

    private static TrabajoDto Dto(Persistence.Modelo.TrabajoProgramado t, DateTime ahoraLocal) => new(
        t.Tipo.ToString(), t.ExpresionCron,
        ExpresionCron.EsValida(t.ExpresionCron, out _) ? ExpresionCron.Parse(t.ExpresionCron).Siguiente(ahoraLocal)?.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture) : null,
        t.Activo, t.SoloDiasHabiles, t.UltimaEjecucionEn, t.UltimoResultado?.ToString());

    private static async Task<IResult> ListarTrabajos(ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var lista = await db.TrabajosProgramados.AsNoTracking().OrderBy(t => t.Tipo).ToListAsync(ct).ConfigureAwait(false);
        var local = reloj.AhoraColombia.DateTime;
        return Results.Ok(lista.Select(t => Dto(t, local)));
    }

    private static async Task<IResult> CambiarTrabajo(string tipo, CambioTrabajo pedido, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        if (!Enum.TryParse<TipoTrabajo>(tipo, true, out var t))
            throw ReglaException.NoEncontrado("El trabajo");
        var trabajo = await db.TrabajosProgramados.FirstAsync(x => x.Tipo == t, ct).ConfigureAwait(false);
        var cron = pedido.ExpresionCron?.Trim() ?? trabajo.ExpresionCron;
        if (!ExpresionCron.EsValida(cron, out var error))
            throw new ReglaException("CRON_INVALIDO", $"La expresión no es válida: {error}");

        var cambios = Auditoria.Diferencias(("expresion_cron", trabajo.ExpresionCron, cron), ("activo", trabajo.Activo, pedido.Activo ?? trabajo.Activo),
            ("solo_dias_habiles", trabajo.SoloDiasHabiles, pedido.SoloDiasHabiles ?? trabajo.SoloDiasHabiles));
        if (cambios.Length > 0)
        {
            trabajo.ExpresionCron = cron;
            trabajo.Activo = pedido.Activo ?? trabajo.Activo;
            trabajo.SoloDiasHabiles = pedido.SoloDiasHabiles ?? trabajo.SoloDiasHabiles;
            trabajo.ActualizadoEn = reloj.Ahora;
            trabajo.ActualizadoPor = usuario.Id;
            auditoria.Registrar(null, "trabajo_programado", trabajo.Tipo, "ACTUALIZAR", cambios);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(Dto(trabajo, reloj.AhoraColombia.DateTime));
    }

    /// <summary>Ejecución manual por empresa y canal, nunca por una sola clave (RN-038). Queda auditada y corre en segundo plano.</summary>
    private static async Task<IResult> Ejecutar(EjecucionManual pedido, ColaTrabajos cola, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, ElegibilidadConsultas elegibilidad, CancellationToken ct)
    {
        if (!Enum.TryParse<CanalIngesta>(pedido.Canal, true, out var canal))
            throw new ReglaException("CANAL_INVALIDO", "Elige SIMIT o municipal, por NIT o por placa.");
        if (await elegibilidad.MotivoManualAsync(pedido.EmpresaId, canal, ct).ConfigureAwait(false) is { } motivo)
            throw ReglaException.Conflicto("CONSULTA_NO_HABILITADA", motivo);
        if (await db.CiclosIngesta.AnyAsync(c => c.Canal == canal && c.Resultado == ResultadoCiclo.EnCurso, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("CICLO_EN_CURSO", $"Ya hay un ciclo de {Reconciliador.Etiqueta(canal)} en curso.");

        auditoria.Registrar(pedido.EmpresaId, "trabajo_programado", CicloDeIngesta.Trabajo(canal), "EJECUTAR_MANUAL", new CambioCampo("empresa", null, pedido.EmpresaId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await cola.EncolarAsync(new SolicitudTrabajo(CicloDeIngesta.Trabajo(canal), Disparo.Manual, pedido.EmpresaId, usuario.Id), ct).ConfigureAwait(false);
        return Results.Accepted();
    }

    /// <summary>Ejecución manual de un trabajo que no es de ingesta (reportes, avisos, reintento de contraventor), opcionalmente para una empresa.</summary>
    private static async Task<IResult> EjecutarTrabajo(string tipo, Guid? empresaId, ColaTrabajos cola, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, CancellationToken ct)
    {
        if (!Enum.TryParse<TipoTrabajo>(tipo, true, out var t) || !Enum.IsDefined(t))
            throw ReglaException.NoEncontrado("El trabajo");
        if (CicloDeIngesta.Canal(t) is not null)
            throw new ReglaException("USAR_EJECUTAR_INGESTA", "Los ciclos de ingesta se ejecutan con /admin/ingesta/ejecutar.");
        auditoria.Registrar(empresaId, "trabajo_programado", t, "EJECUTAR_MANUAL", new CambioCampo("empresa", null, empresaId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await cola.EncolarAsync(new SolicitudTrabajo(t, Disparo.Manual, empresaId, usuario.Id), ct).ConfigureAwait(false);
        return Results.Accepted();
    }

    private static async Task<IResult> ListarCiclos(string? canal, string? resultado, Guid? empresa, DateOnly? desde, DateOnly? hasta, int? pagina, int? tamano, ComparendosDb db, CancellationToken ct)
    {
        var (p, t) = Paginacion.Normalizar(pagina, tamano, 25);
        var q = db.CiclosIngesta.AsNoTracking();
        if (Enum.TryParse<CanalIngesta>(canal, true, out var c))
            q = q.Where(x => x.Canal == c);
        if (Enum.TryParse<ResultadoCiclo>(resultado, true, out var r))
            q = q.Where(x => x.Resultado == r);
        if (empresa is { } e)
            q = q.Where(x => x.TenantIdManual == e || db.LogMigracion.Any(l => l.CicloId == x.Id && l.TenantId == e));
        if (desde is { } d)
            q = q.Where(x => x.InicioEn >= new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime());
        if (hasta is { } h)
            q = q.Where(x => x.InicioEn < new DateTimeOffset(h.AddDays(1).ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime());

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var items = await q.OrderByDescending(x => x.InicioEn).Skip((p - 1) * t).Take(t)
            .Select(x => new CicloDto(x.Id, x.Canal.ToString(), x.Disparo.ToString(), x.TenantIdManual, x.InicioEn, x.FinEn, x.ClavesTotal, x.ClavesError, x.Resultado.ToString(), x.MensajeError))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new Pagina<CicloDto>(items, total, p, t));
    }

    private static async Task<IResult> DetalleCiclo(Guid id, ComparendosDb db, CancellationToken ct)
    {
        var ciclo = await db.CiclosIngesta.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El ciclo");
        var logs = await db.LogMigracion.AsNoTracking().Where(l => l.CicloId == id).OrderBy(l => l.TenantId).ThenBy(l => l.Clave).ToListAsync(ct).ConfigureAwait(false);
        var consultas = await db.ConsultasFuente.AsNoTracking().Where(c => c.CicloId == id).OrderBy(c => c.ClaveConsultada)
            .Select(c => new { c.Id, c.ClaveConsultada, c.MunicipioCodigo, c.HttpStatus, resultado = c.Resultado.ToString(), tipoError = c.TipoError.ToString(), c.MensajeError, c.CantidadItems, c.DuracionMs, tieneRespuesta = c.RespuestaArchivoId != null })
            .ToListAsync(ct).ConfigureAwait(false);
        var empresas = await db.ConfiguracionesEmpresa.AsNoTracking().ToDictionaryAsync(e => e.TenantId, e => e.EmpresaNombre, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            ciclo = new CicloDto(ciclo.Id, ciclo.Canal.ToString(), ciclo.Disparo.ToString(), ciclo.TenantIdManual, ciclo.InicioEn, ciclo.FinEn, ciclo.ClavesTotal, ciclo.ClavesError, ciclo.Resultado.ToString(), ciclo.MensajeError),
            logs = logs.Select(l => new
            {
                l.Id,
                empresaId = l.TenantId,
                empresa = empresas.GetValueOrDefault(l.TenantId),
                l.Clave,
                l.MunicipioCodigo,
                l.CantidadObtenidas,
                l.CantidadNuevas,
                l.CantidadActualizadas,
                l.CantidadSinCambios,
                l.CantidadDuplicadasOtraFuente,
                l.CantidadInactivadas,
                l.CantidadReactivadas,
                l.CantidadDpFavorableAutomatico,
                resultado = l.Resultado.ToString(),
                l.MensajeError,
            }),
            consultas,
        });
    }

    private static async Task<IResult> RespuestaCruda(Guid id, ComparendosDb db, IAlmacenArchivos almacen, CancellationToken ct)
    {
        var archivo = await (from c in db.ConsultasFuente
                             join a in db.Archivos on c.RespuestaArchivoId equals a.Id
                             where c.Id == id
                             select a).AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La respuesta");
        var bytes = await almacen.LeerAsync(archivo.Referencia, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La respuesta");
        return Results.File(bytes, archivo.ContentType, archivo.NombreOriginal);
    }
}
