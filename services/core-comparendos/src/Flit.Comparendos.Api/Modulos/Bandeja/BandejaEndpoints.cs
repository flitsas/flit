using Flit.Comparendos.Api.Infraestructura.Excel;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Bandeja;

/// <summary>Bandeja unificada (HU #13496): lista filtrada y paginada, contadores de filtros rápidos y exportación.</summary>
public static class BandejaEndpoints
{
    /// <summary>Los conteos de los filtros rápidos y, desde la HU #13869, el de cada estado legal (tarjetas de la bandeja).</summary>
    public sealed record Contadores(int Total, int ProximosAVencer, int SinContraventor, int RechazadosOt, IReadOnlyDictionary<string, int> EstadosLegales);

    private static readonly string[] EstadosLegales = ["SinNotificar", "EnTiempo", "Vencido", "Caducado", "Prescrito"];

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/comparendos", Listar).RequirePermiso(Permisos.BandejaVer);
        api.MapGet("/comparendos/contadores", ContarRapidos).RequirePermiso(Permisos.BandejaVer);
        api.MapGet("/comparendos/excel", Exportar).RequirePermiso(Permisos.BandejaExportar);
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

    /// <summary>
    /// Conteo de cada filtro rápido sobre los filtros vigentes (sin el filtro rápido elegido) y de cada estado legal sobre
    /// los filtros vigentes sin el de estado legal: así cada tarjeta dice cuántos quedarían al elegirla.
    /// </summary>
    private static async Task<IResult> ContarRapidos(HttpRequest request, UsuarioActual usuario, ConsultaBandeja consulta, CancellationToken ct)
    {
        var vigentes = FiltrosBandeja.Desde(request);
        var f = vigentes with { Rapido = null };
        var tenant = usuario.EmpresaLectura(f.Empresa);
        var h = await consulta.HoyAsync(ct).ConfigureAwait(false);
        var q = consulta.Filtrar(tenant, f, h);
        var estados = new Dictionary<string, int>();
        foreach (var estado in EstadosLegales)
            estados[estado] = await consulta.Filtrar(tenant, vigentes with { EstadoLegal = [estado] }, h).CountAsync(ct).ConfigureAwait(false);
        return Results.Ok(new Contadores(
            await q.CountAsync(ct).ConfigureAwait(false),
            await consulta.AplicarRapido(q, "ProximosAVencer", h).CountAsync(ct).ConfigureAwait(false),
            await consulta.AplicarRapido(q, "SinContraventor", h).CountAsync(ct).ConfigureAwait(false),
            await consulta.AplicarRapido(q, "RechazadosOt", h).CountAsync(ct).ConfigureAwait(false),
            estados));
    }

    /// <summary>
    /// Una parte de hasta 5.000 filas de lo filtrado (HU #13871): ?parte=N (desde 1) y ?columnas=clave,clave… con las
    /// columnas visibles de la tabla, en su orden. El navegador pide todas las partes en el mismo clic.
    /// </summary>
    private static async Task<IResult> Exportar(HttpRequest request, UsuarioActual usuario, ExportacionBandeja exportacion, Reloj reloj, CancellationToken ct)
    {
        var f = FiltrosBandeja.Desde(request);
        var tenant = usuario.EmpresaLectura(f.Empresa);
        var parte = int.TryParse(request.Query["parte"], out var n) && n > 0 ? n : 1;
        var columnas = request.Query["columnas"].SelectMany(x => (x ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        var bytes = await exportacion.GenerarAsync(tenant, f, parte, columnas, ct).ConfigureAwait(false);
        return Results.File(bytes, TabularWorkbookWriter.ContentType, $"comparendos-{reloj.Hoy:yyyyMMdd}.xlsx");
    }
}
