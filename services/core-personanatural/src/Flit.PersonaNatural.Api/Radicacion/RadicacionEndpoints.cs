using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>
/// <c>POST /solicitudes/{id}/radicar</c> (HU #14049): exige la sesión de ESA solicitud y la cabecera <c>Idempotency-Key</c>, y
/// responde <c>202</c> con la solicitud en <c>en_cola</c>. La entrega a FLIT 2 es asíncrona (outbox): el ciudadano sigue el
/// estado, no espera al trámite.
/// </summary>
public static class RadicacionEndpoints
{
    public const string CabeceraIdempotencia = "Idempotency-Key";

    /// <summary>
    /// Registra el servicio de radicar y su compuerta de identidad (HU #14046, <see cref="ICompuertaDeRadicacion"/>): una sola
    /// implementación, sin respaldo más débil que pueda ganar por el orden de registro.
    /// </summary>
    public static IServiceCollection AddRadicacionPortal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<RadicacionService>();
        services.AddCompuertaDeRadicacion();
        return services;
    }

    public static IEndpointRouteBuilder MapRadicacionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var conSesion = app.MapGroup(SesionEndpoints.Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp).MapGroup("/solicitudes/{id:guid}");
        conSesion.AddEndpointFilter(Radicacion.SesionTrasRadicar.Filtro);

        conSesion.MapPost("/radicar", (Guid id, HttpContext http, RadicacionService s, CancellationToken ct) =>
            SolicitudesEndpoints.Atender(async () =>
                Results.Json(await s.RadicarAsync(id, http.Request.Headers[CabeceraIdempotencia].ToString(), ct).ConfigureAwait(false), statusCode: StatusCodes.Status202Accepted))).WithMetadata(PermiteTrasRadicar.Instancia);

        return app;
    }
}
