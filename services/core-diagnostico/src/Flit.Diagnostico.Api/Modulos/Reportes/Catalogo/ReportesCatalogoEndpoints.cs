using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Rutas del módulo de reportes de la compañía (HU #14579; contrato-api §2–§5; SP-1, SP-5, SP-6, SP-14): R0 <c>GET /reportes</c> y R1
/// <c>GET /reportes/{tipo}</c> con <c>diagnostico.reportes.ver</c> y <c>no-store</c>; R2/R3 <c>/reportes/{tipo}/excel|pdf</c> (BE-3) además con
/// <c>diagnostico.reportes.descargar</c> y <c>.DescargaSegura()</c>. Filtro de tenant normal y visibilidad de
/// <c>AccesoLote</c>: el SuperAdmin ve solo su compañía; <c>?empresa=</c> da 400. Cada página deja su fila en <c>accesos_reporte</c>
/// antes de descifrar. La lógica vive en <see cref="MotorReportes"/>, que el backoffice reutiliza con su propio contexto.
/// </summary>
internal static class ReportesCatalogoEndpoints
{
    public const string Ruta = "/reportes";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(Ruta, Catalogo).RequiereVerReportes().SinCacheEnRespuesta().WithName("CatalogoReportesCompania");
        api.MapGet(Ruta + "/{tipo}", PantallaAsync).RequiereVerReportes().SinCacheEnRespuesta().WithName("ReporteCompania");
        // BE-3 (R2/R3): además reportes.descargar y las cabeceras de descarga segura (SP-14).
        api.MapGet(Ruta + "/{tipo}/excel", (string tipo, HttpRequest request, DiagnosticoDb db, UsuarioActual usuario, ExportadorReportes exportador,
                RegistroAccesosReporte registro, TimeProvider reloj, CancellationToken ct) =>
                ExportarAsync(tipo, request, db, usuario, exportador, registro, reloj, FormatoReporte.Xlsx, ct))
            .RequiereVerReportes().RequirePermiso(Permisos.ReportesDescargar).DescargaSegura().WithName("ExportarReporteExcel");
        api.MapGet(Ruta + "/{tipo}/pdf", (string tipo, HttpRequest request, DiagnosticoDb db, UsuarioActual usuario, ExportadorReportes exportador,
                RegistroAccesosReporte registro, TimeProvider reloj, CancellationToken ct) =>
                ExportarAsync(tipo, request, db, usuario, exportador, registro, reloj, FormatoReporte.Pdf, ct))
            .RequiereVerReportes().RequirePermiso(Permisos.ReportesDescargar).DescargaSegura().WithName("ExportarReportePdf");
    }

    private static IResult Catalogo(IOptions<OpcionesReportes> opciones) =>
        Results.Ok(CatalogoReportes.Dto(AlcanceReporte.Compania, opciones.Value));

    private static async Task<IResult> PantallaAsync(
        string tipo, HttpRequest request, DiagnosticoDb db, UsuarioActual usuario, MotorReportes motor, RegistroAccesosReporte registro,
        TimeProvider reloj, CancellationToken ct)
    {
        var filtro = LeerFiltro(tipo, request, reloj, paginar: true);
        var contexto = new ContextoReporte(AlcanceReporte.Compania, await FuentesReporte.DeLaCompaniaAsync(db, usuario, ct).ConfigureAwait(false));
        return Results.Ok(await motor.PaginaAsync(contexto, filtro, (s, _, c) => registro.RegistrarAsync(s, c), ct).ConfigureAwait(false));
    }

    private static async Task<IResult> ExportarAsync(
        string tipo, HttpRequest request, DiagnosticoDb db, UsuarioActual usuario, ExportadorReportes exportador, RegistroAccesosReporte registro,
        TimeProvider reloj, FormatoReporte formato, CancellationToken ct)
    {
        var filtro = LeerFiltro(tipo, request, reloj, paginar: false);
        var contexto = new ContextoReporte(AlcanceReporte.Compania, await FuentesReporte.DeLaCompaniaAsync(db, usuario, ct).ConfigureAwait(false));
        return await exportador.ExportarAsync(request.HttpContext.Response, contexto, filtro, formato, (s, _, c) => registro.RegistrarAsync(s, c), ct)
            .ConfigureAwait(false);
    }

    internal static FiltroReporte LeerFiltro(string tipo, HttpRequest request, TimeProvider reloj, bool paginar) =>
        FiltroReporte.Leer(CatalogoReportes.LeerTipo(tipo, AlcanceReporte.Compania), request.Query, FiltroReporte.Hoy(reloj), rechazarEmpresa: true, paginar);
}
