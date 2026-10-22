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
/// </list>
/// </summary>
internal static class ReportesEndpoints
{
    public const string RutaPdfVehiculo = DiagnosticosEndpoints.Ruta + "/{id:guid}/vehiculos/{vid:guid}/pdf";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(RutaPdfVehiculo, PdfAsync).RequirePermiso(Permisos.ReportesDescargar).WithName("DescargarPdfVehiculo");
    }

    private static async Task<IResult> PdfAsync(Guid id, Guid vid, DescargaPdfVehiculo descarga, HttpResponse respuesta, CancellationToken ct)
    {
        var archivo = await descarga.ObtenerAsync(id, vid, ct).ConfigureAwait(false);
        // Lleva documentos enmascarados y datos de la compañía: ningún intermediario lo guarda.
        respuesta.Headers.CacheControl = "no-store";
        return Results.File(archivo.Contenido, archivo.ContentType, archivo.Nombre);
    }
}
