using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Pagos;

/// <summary>
/// Rutas del paso Pago del punto, bajo <c>/punto/pago</c>. Exigen la cookie del punto (organismo fijo) y el token de la sesión del
/// trámite (<c>Authorization: Bearer</c>), igual que la identidad y los requisitos. Los errores salen como <c>problem+json</c> con
/// <c>code</c> estable.
/// </summary>
public static class PagoPuntoEndpoints
{
    public static IServiceCollection AddPagosDelPunto(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<CotizadorDelPunto>();
        services.TryAddScoped<IRegistroDePagos, RegistroDePagos>();
        services.TryAddScoped<Radicacion.RadicacionDePagadasService>();
        services.TryAddScoped<PagoPuntoService>();
        services.TryAddScoped<ICorreoDeCierre, CorreoDeCierre>();
        services.TryAddScoped<BloqueoConsultaCierre>();
        services.TryAddScoped<CierrePuntoService>();
        return services;
    }

    public static RouteGroupBuilder MapPagoPunto(this RouteGroupBuilder punto)
    {
        ArgumentNullException.ThrowIfNull(punto);
        var pago = punto.MapGroup("/pago");

        // El servicio se resuelve desde RequestServices para que el host de pruebas del canal (sin base) pueda mapear el grupo.
        // Pasar a Pago: 409 identity_pending / requisitos_incompletos si falta algo, sin cambiar el estado.
        pago.MapPost("/iniciar", (HttpContext http, CancellationToken ct) => Atender(http, async (s, sesion, token) =>
            Results.Json(await s.IniciarAsync(sesion, token, ct).ConfigureAwait(false), PuntoEndpoints.Json)));

        // Se consulta cada pocos segundos: estado, desglose y total vigentes, vencimiento e identidades pendientes.
        pago.MapGet(string.Empty, (HttpContext http, CancellationToken ct) => Atender(http, async (s, sesion, token) =>
            Results.Json(await s.EstadoAsync(sesion, token, ct).ConfigureAwait(false), PuntoEndpoints.Json)));

        pago.MapPost("/caja", (HttpContext http, CancellationToken ct) => Atender(http, async (s, sesion, token) =>
            Results.Json(await s.HabilitarCajaAsync(sesion, token, ct).ConfigureAwait(false), PuntoEndpoints.Json)));

        // Sin cuerpo: el cliente nunca envía el valor; el servidor recalcula el total y solo acepta $0.
        pago.MapPost("/confirmar", (HttpContext http, CancellationToken ct) => Atender(http, async (s, sesion, token) =>
            Results.Json(await s.ConfirmarSinCobroAsync(sesion, token, ct).ConfigureAwait(false), PuntoEndpoints.Json)));

        // HU #14464: el cierre en una sola llamada (sesión del trámite). 409 pago_pendiente si aún no hay pago confirmado.
        pago.MapGet("/cierre", (HttpContext http, CancellationToken ct) => AtenderCierre(http, async (s, sesion, token) =>
            Results.Json(await s.DelTramiteAsync(sesion, token, ct).ConfigureAwait(false), PuntoEndpoints.Json)));

        // Para «Consultar» del punto: quien vuelve con el código público ve el mismo cierre (solo cookie del punto, mismo organismo).
        // Freno a la enumeración de códigos secuenciales: 10/min por cookie + IP y bloqueo de 10 min tras 5 fallos seguidos (429).
        // Todo fallo (no existe, otro organismo, sin pago) es el mismo 404 no_encontrado.
        pago.MapGet("/cierre/{codigo}", (string codigo, HttpContext http, CancellationToken ct) => AtenderCierre(http, async (s, sesion, _) =>
        {
            var ipHash = http.RequestServices.GetRequiredService<PiiHasher>().HashIp(http.Connection.RemoteIpAddress?.ToString());
            if (await s.EsperaDeConsultaAsync(sesion, ipHash, ct).ConfigureAwait(false) is { } espera)
            {
                http.Response.Headers.RetryAfter = Math.Ceiling(espera.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return SesionEndpoints.Problema(StatusCodes.Status429TooManyRequests, "Demasiados intentos", "Espere unos minutos antes de volver a intentar.", CierrePuntoService.CodigoConsultaBloqueada);
            }

            return Results.Json(await s.PorCodigoAsync(sesion, codigo, ipHash, ct).ConfigureAwait(false), PuntoEndpoints.Json);
        })).RequireRateLimiting(AbusoEndpoints.PoliticaCierrePorCodigo);

        pago.AddEndpointFilter(PuntoEndpoints.ConSesionDePunto);
        return punto;
    }

    private static Task<IResult> AtenderCierre(HttpContext http, Func<CierrePuntoService, Punto.OrganismoPunto, string?, Task<IResult>> accion)
    {
        http.Response.Headers.CacheControl = "no-store";
        var servicio = http.RequestServices.GetRequiredService<CierrePuntoService>();
        return SolicitudesEndpoints.Atender(() => accion(servicio, PuntoEndpoints.Sesion(http), PuntoEndpoints.TokenSolicitud(http)));
    }

    private static Task<IResult> Atender(HttpContext http, Func<PagoPuntoService, Punto.OrganismoPunto, string?, Task<IResult>> accion)
    {
        http.Response.Headers.CacheControl = "no-store";
        var servicio = http.RequestServices.GetRequiredService<PagoPuntoService>();
        return SolicitudesEndpoints.Atender(() => accion(servicio, PuntoEndpoints.Sesion(http), PuntoEndpoints.TokenSolicitud(http)));
    }
}
