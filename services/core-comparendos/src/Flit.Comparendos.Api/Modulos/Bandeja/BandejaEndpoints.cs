using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Infraestructura.Excel;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Bandeja;

/// <summary>Bandeja unificada (HU #13496): lista filtrada y paginada, contadores de filtros rápidos y exportación.</summary>
public static class BandejaEndpoints
{
    public sealed record Contadores(int Total, int ProximosAVencer, int SinContraventor, int RechazadosOt);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/comparendos", Listar).RequirePermiso(Permisos.BandejaVer);
        api.MapGet("/comparendos/contadores", ContarRapidos).RequirePermiso(Permisos.BandejaVer);
        api.MapGet("/comparendos/excel", Exportar).RequirePermiso(Permisos.BandejaExportar);
        api.MapGet("/exportaciones", MisExportaciones).RequireAuthorization(AutorizacionExtensions.PoliticaUsuario);
        api.MapGet("/exportaciones/{id:guid}/archivo", DescargarExportacion).RequireAuthorization(AutorizacionExtensions.PoliticaUsuario);
    }

    private static async Task<IResult> Listar(HttpRequest request, UsuarioActual usuario, ConsultaBandeja consulta, CancellationToken ct)
    {
        var f = FiltrosBandeja.Desde(request);
        var tenant = usuario.EmpresaLectura(f.Empresa);
        var (p, t) = Paginacion.Normalizar(f.Pagina, f.Tamano, 50);
        var h = await consulta.HoyAsync(ct).ConfigureAwait(false);
        var q = consulta.Filtrar(tenant, f, h);
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var filas = await consulta.ProyectarAsync(ConsultaBandeja.Ordenar(q, f.Orden, f.Descendente, h.Dia).Skip((p - 1) * t).Take(t), h, ct).ConfigureAwait(false);
        return Results.Ok(new Pagina<FilaBandeja>(filas, total, p, t));
    }

    /// <summary>Conteo de cada filtro rápido sobre los filtros vigentes (sin el filtro rápido elegido).</summary>
    private static async Task<IResult> ContarRapidos(HttpRequest request, UsuarioActual usuario, ConsultaBandeja consulta, CancellationToken ct)
    {
        var f = FiltrosBandeja.Desde(request) with { Rapido = null };
        var tenant = usuario.EmpresaLectura(f.Empresa);
        var h = await consulta.HoyAsync(ct).ConfigureAwait(false);
        var q = consulta.Filtrar(tenant, f, h);
        return Results.Ok(new Contadores(
            await q.CountAsync(ct).ConfigureAwait(false),
            await consulta.AplicarRapido(q, "ProximosAVencer", h).CountAsync(ct).ConfigureAwait(false),
            await consulta.AplicarRapido(q, "SinContraventor", h).CountAsync(ct).ConfigureAwait(false),
            await consulta.AplicarRapido(q, "RechazadosOt", h).CountAsync(ct).ConfigureAwait(false)));
    }

    private static async Task<IResult> Exportar(HttpRequest request, UsuarioActual usuario, ConsultaBandeja consulta, ExportacionBandeja exportacion, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var f = FiltrosBandeja.Desde(request);
        var tenant = usuario.EmpresaLectura(f.Empresa);
        var h = await consulta.HoyAsync(ct).ConfigureAwait(false);
        var total = await consulta.Filtrar(tenant, f, h).CountAsync(ct).ConfigureAwait(false);
        if (total <= ExportacionBandeja.LimiteDirecto)
        {
            var bytes = await exportacion.GenerarAsync(tenant, f, ct).ConfigureAwait(false);
            return Results.File(bytes, TabularWorkbookWriter.ContentType, $"comparendos-{reloj.Hoy:yyyyMMdd}.xlsx");
        }

        var e = new Exportacion
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant ?? Guid.Empty,
            SolicitadaPor = usuario.Id,
            Filtros = ExportacionBandeja.Serializar(tenant, f, usuario.Email),
            TotalFilas = total,
            CreadoEn = reloj.Ahora,
        };
        db.Exportaciones.Add(e);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Accepted($"{Program.Prefijo}/exportaciones/{e.Id}", new { id = e.Id, total, mensaje = "La exportación es grande: te avisamos por correo cuando esté lista." });
    }

    private static async Task<IResult> MisExportaciones(UsuarioActual usuario, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var lista = await db.Exportaciones.AsNoTracking().Where(e => e.SolicitadaPor == usuario.Id).OrderByDescending(e => e.CreadoEn).Take(50).ToListAsync(ct).ConfigureAwait(false);
        // Los filtros con que se pidió cada una (§7.26), con el nombre de los NIT y organismos para mostrarlos.
        var filtros = lista.ToDictionary(e => e.Id, e => ExportacionBandeja.Deserializar(e.Filtros).Filtros);
        var nits = filtros.Values.SelectMany(f => f.Nits ?? []).Distinct().ToList();
        var ots = filtros.Values.SelectMany(f => f.Organismos ?? []).Distinct().ToList();
        var nombres = (await db.IdentificacionesMonitoreadas.AsNoTracking().Where(i => nits.Contains(i.Id)).Select(i => new { i.Id, Texto = i.NumeroBase }).ToListAsync(ct).ConfigureAwait(false))
            .Concat(await db.OrganismosTransito.AsNoTracking().Where(o => ots.Contains(o.Id)).Select(o => new { o.Id, Texto = o.Nombre }).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(x => x.Id, x => x.Texto);
        return Results.Ok(lista.Select(e => new
        {
            e.Id,
            filtros = filtros[e.Id],
            nombres = (filtros[e.Id].Nits ?? []).Concat(filtros[e.Id].Organismos ?? []).Where(nombres.ContainsKey).ToDictionary(id => id, id => nombres[id]),
            e.TotalFilas,
            estado = (e.Estado == EstadoExportacion.Lista && e.ExpiraEn < reloj.Ahora ? EstadoExportacion.Expirada : e.Estado).ToString(),
            e.CreadoEn,
            e.TerminadoEn,
            e.ExpiraEn,
        }));
    }

    private static async Task<IResult> DescargarExportacion(Guid id, UsuarioActual usuario, ComparendosDb db, IAlmacenArchivos almacen, Reloj reloj, CancellationToken ct)
    {
        var e = await db.Exportaciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.SolicitadaPor == usuario.Id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La exportación");
        if (e.Estado != EstadoExportacion.Lista || e.ArchivoId is not { } archivoId || e.ExpiraEn < reloj.Ahora)
            throw ReglaException.Conflicto("EXPORTACION_NO_DISPONIBLE", "La exportación no está lista o ya venció.");
        var archivo = await db.Archivos.AsNoTracking().FirstAsync(a => a.Id == archivoId, ct).ConfigureAwait(false);
        var bytes = await almacen.LeerAsync(archivo.Referencia, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El archivo");
        return Results.File(bytes, archivo.ContentType, archivo.NombreOriginal);
    }
}
