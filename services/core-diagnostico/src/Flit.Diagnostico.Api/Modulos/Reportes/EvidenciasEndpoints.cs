using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// ZIP de los PDF del lote en segundo plano (HU #13850, F07-H5, decisión Q2 del 2026-10-22), con <c>diagnostico.reportes.descargar</c>
/// y la visibilidad de <c>AccesoLote</c> (<see cref="SolicitudZip"/>):
/// <list type="bullet">
/// <item><c>POST /diagnosticos/{id}/evidencias</c> con <c>{"tipo":"ZIP"}</c>: 202 + <c>Location</c> del estado si queda en la cola
/// (nuevo o el mismo que ya estaba con esa huella), 200 si ya hay uno LISTA y vigente con la misma huella.</item>
/// <item><c>GET /diagnosticos/{id}/evidencias/{eid}</c>: el estado del ZIP (el frontend lo sondea).</item>
/// <item><c>GET /diagnosticos/{id}/evidencias/{eid}/archivo</c>: <c>application/zip</c> como adjunto <c>diagnostico-{8}.zip</c>, sin
/// caché; 409 <c>EVIDENCIA_NO_LISTA</c> si aún no está LISTA.</item>
/// </list>
/// Lo genera <see cref="EmpaquetadorZip"/> desde la cola (<c>TrabajadorEvidencias</c>).
/// </summary>
internal static class EvidenciasEndpoints
{
    public const string RutaEvidencias = DiagnosticosEndpoints.Ruta + "/{id:guid}/evidencias";

    public const string RutaEstadoEvidencia = RutaEvidencias + "/{eid:guid}";

    public const string RutaArchivoEvidencia = RutaEstadoEvidencia + "/archivo";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapPost(RutaEvidencias, PedirAsync).RequirePermiso(Permisos.ReportesDescargar).WithName("PedirEvidenciaLote");
        api.MapGet(RutaEstadoEvidencia, EstadoAsync).RequirePermiso(Permisos.ReportesDescargar).WithName("EstadoEvidenciaLote");
        api.MapGet(RutaArchivoEvidencia, ArchivoAsync).RequirePermiso(Permisos.ReportesDescargar).WithName("DescargarEvidenciaLote");
    }

    private static async Task<IResult> PedirAsync(Guid id, SolicitudEvidenciaDto? cuerpo, SolicitudZip solicitud, CancellationToken ct)
    {
        var pedido = await solicitud.PedirAsync(id, cuerpo?.Tipo, ct).ConfigureAwait(false);
        return pedido.Listo
            ? Results.Ok(pedido.Estado)
            : Results.Accepted($"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/evidencias/{pedido.Estado.Id}", pedido.Estado);
    }

    private static async Task<IResult> EstadoAsync(Guid id, Guid eid, SolicitudZip solicitud, HttpResponse respuesta, CancellationToken ct)
    {
        var estado = await solicitud.EstadoAsync(id, eid, ct).ConfigureAwait(false);
        // Se sondea: cada consulta debe llegar al servicio.
        respuesta.Headers.CacheControl = "no-store";
        return Results.Ok(estado);
    }

    private static async Task<IResult> ArchivoAsync(Guid id, Guid eid, SolicitudZip solicitud, HttpResponse respuesta, CancellationToken ct)
    {
        var (contenido, nombre) = await solicitud.ArchivoAsync(id, eid, ct).ConfigureAwait(false);
        // Lleva documentos enmascarados y datos de la compañía: ningún intermediario lo guarda.
        respuesta.Headers.CacheControl = "no-store";
        return Results.Stream(contenido, EmpaquetadorZip.ContentType, nombre);
    }
}
