using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>
/// Saneamiento del cliente (HU #14576, plan §6.1, §5.2, ADR-0087), todo con el token Bearer del producto (el grupo exige la
/// autenticación de la plataforma; sin cookies, así que no aplica antiforgery, SP-20):
/// <list type="bullet">
/// <item><c>PUT /diagnosticos/{id}/vehiculos/{vid}/saneamiento</c>: marca (201, o 200 si ya estaba SOLICITADO).</item>
/// <item><c>DELETE …/saneamiento</c>: desmarca en SOLICITADO (204).</item>
/// <item><c>POST /saneamientos/solicitudes</c>: masivo de 1 a 500.</item>
/// <item><c>GET …/saneamiento</c>: detalle para la compañía (§6.3), con <c>diagnostico.diagnosticos.ver</c>.</item>
/// </list>
/// Marcar, desmarcar y el masivo exigen <c>diagnostico.saneamiento.solicitar</c> (el SuperAdmin pasa por la regla general, decisión
/// E3, y opera en la empresa de su sesión) y responden 403 <see cref="CodigoDesdeBackoffice"/> con <c>?empresa=</c>: desde el
/// backoffice no se marca.
/// </summary>
internal static class SaneamientoEndpoints
{
    public const string Ruta = DiagnosticosEndpoints.Ruta + "/{id:guid}/vehiculos/{vid:guid}/saneamiento";

    public const string RutaSolicitudes = "/saneamientos/solicitudes";

    public const string CodigoDesdeBackoffice = "SANEAMIENTO_DESDE_BACKOFFICE";

    private const string MensajeDesdeBackoffice = "Desde el backoffice no se marca el saneamiento de un vehículo.";

    public static IServiceCollection AddSaneamiento(this IServiceCollection services)
    {
        services.AddScoped<SolicitudSaneamiento>();
        // Brief C5: las lecturas del vehículo para los hallazgos (las usa la toma de BE-B).
        services.AddScoped<Dictamen.LecturasVehiculo>();
        return services;
    }

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapPut(Ruta, (Guid id, Guid vid, SolicitudSaneamiento solicitud, CancellationToken ct) => solicitud.MarcarAsync(id, vid, ct))
            .RequirePermiso(Permisos.SaneamientoSolicitar)
            .SinBackoffice(CodigoDesdeBackoffice, MensajeDesdeBackoffice)
            .WithName("MarcarSaneamientoVehiculo");
        api.MapDelete(Ruta, (Guid id, Guid vid, SolicitudSaneamiento solicitud, CancellationToken ct) => solicitud.DesmarcarAsync(id, vid, ct))
            .RequirePermiso(Permisos.SaneamientoSolicitar)
            .SinBackoffice(CodigoDesdeBackoffice, MensajeDesdeBackoffice)
            .WithName("DesmarcarSaneamientoVehiculo");
        api.MapPost(RutaSolicitudes, (HttpRequest request, SolicitudSaneamiento solicitud, CancellationToken ct) => solicitud.SolicitarMasivoAsync(request, ct))
            .RequirePermiso(Permisos.SaneamientoSolicitar)
            .SinBackoffice(CodigoDesdeBackoffice, MensajeDesdeBackoffice)
            .WithName("SolicitarSaneamientosMasivo");
        api.MapGet(Ruta, async (Guid id, Guid vid, SolicitudSaneamiento solicitud, CancellationToken ct) =>
                Results.Ok(await solicitud.DetalleAsync(id, vid, ct).ConfigureAwait(false)))
            .RequirePermiso(Permisos.DiagnosticosVer)
            .SinCacheEnRespuesta()
            .WithName("ObtenerSaneamientoVehiculo");
    }
}
