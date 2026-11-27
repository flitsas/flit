using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Descargas de evidencias de la compañía (HU #13848, F07-H3), con <c>diagnostico.reportes.descargar</c> y la visibilidad de
/// <c>AccesoLote</c> (otra compañía, PROPIOS de otro usuario o vehículo de otro lote: 404 sin datos):
/// <list type="bullet">
/// <item><c>GET /diagnosticos/{id}/vehiculos/{vid}/pdf</c>: PDF corporativo del vehículo (<see cref="DescargaPdfVehiculo"/>), como
/// adjunto <c>diagnostico-{8 primeros del id}-{placa o VIN}.pdf</c>; 409 <c>VEHICULO_NO_TERMINADO</c> si aún no se puede emitir
/// (<see cref="EmisionPdf"/>). Misma huella → los mismos bytes guardados.</item>
/// <item><c>GET /diagnosticos/{id}/excel</c> (HU #13849, F07-H4): Excel del lote (<see cref="DescargaExcelLote"/>), como adjunto
/// <c>diagnostico-{8 primeros del id}.xlsx</c> (sin placa ni documento); 404 antes que 409 <c>LOTE_SIN_LANZAR</c> (lote CREADO). Misma
/// huella → los mismos bytes. HU #14752 (SP-8): lleva datos personales en claro (D3), así que sale con <c>no-store</c>,
/// <c>Pragma: no-cache</c> y <c>nosniff</c>.</item>
/// </list>
/// </summary>
internal static class ReportesEndpoints
{
    public const string RutaPdfVehiculo = DiagnosticosEndpoints.Ruta + "/{id:guid}/vehiculos/{vid:guid}/pdf";

    public const string RutaExcelLote = DiagnosticosEndpoints.Ruta + "/{id:guid}/excel";

    public const string RutaLiquidacionImpuesto =
        DiagnosticosEndpoints.Ruta + "/{id:guid}/vehiculos/{vid:guid}/impuesto/liquidaciones/{vigencia}";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(RutaPdfVehiculo, PdfAsync).RequirePermiso(Permisos.ReportesDescargar).WithName("DescargarPdfVehiculo");
        api.MapGet(RutaExcelLote, ExcelAsync).RequirePermiso(Permisos.ReportesDescargar).WithName("DescargarExcelLote");
        api.MapGet(RutaLiquidacionImpuesto, LiquidacionAsync).RequirePermiso(Permisos.ReportesDescargar).WithName("DescargarLiquidacionImpuesto");
    }

    private static async Task<IResult> PdfAsync(Guid id, Guid vid, DescargaPdfVehiculo descarga, HttpResponse respuesta, CancellationToken ct)
    {
        var archivo = await descarga.ObtenerAsync(id, vid, ct).ConfigureAwait(false);
        // Lleva documentos enmascarados y datos de la compañía: ningún intermediario lo guarda.
        respuesta.Headers.CacheControl = "no-store";
        return Results.File(archivo.Contenido, archivo.ContentType, archivo.Nombre);
    }

    private static async Task<IResult> ExcelAsync(Guid id, DescargaExcelLote descarga, HttpResponse respuesta, CancellationToken ct)
    {
        var archivo = await descarga.ObtenerAsync(id, ct).ConfigureAwait(false);
        // HU #14752 (SP-8): el Excel v4 lleva en claro los datos de D3 (ADR-0076, enmienda 2026-11-27) y datos de la compañía: sin
        // caché intermedia ni detección de tipo por el navegador.
        respuesta.Headers.CacheControl = "no-store";
        respuesta.Headers.Pragma = "no-cache";
        respuesta.Headers.XContentTypeOptions = "nosniff";
        return Results.File(archivo.Contenido, archivo.ContentType, archivo.Nombre);
    }

    private static async Task<IResult> LiquidacionAsync(
        Guid id, Guid vid, string vigencia, DescargaLiquidacionImpuesto descarga, HttpResponse respuesta, CancellationToken ct)
    {
        var archivo = await descarga.ObtenerAsync(id, vid, vigencia, ct).ConfigureAwait(false);
        respuesta.Headers.CacheControl = "no-store";
        return Results.File(archivo.Contenido, archivo.ContentType, archivo.Nombre);
    }
}
