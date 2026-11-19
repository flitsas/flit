using System.Globalization;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Sesiones;

/// <summary>
/// Endpoints públicos de la sesión por trámite (HU #14035, ADR-0075). Sin cuenta ni token de plataforma: lo que abre el
/// trámite es un token opaco ligado a UNA solicitud. Los errores son <c>application/problem+json</c> con un <c>code</c> estable.
/// </summary>
public static class SesionEndpoints
{
    public const string Base = "/api/v1/persona-natural";
    public const string CodigoNoEncontrada = "solicitud_no_encontrada";
    public const string CodigoBloqueado = "reanudar_bloqueado";
    public const string CodigoSesionVencida = "session_expired";
    public const string CodigoSesionInvalida = "session_invalid";
    public const string CodigoFueraDeAlcance = "session_scope_mismatch";
    public const string AccionFueraDeAlcance = "sesion_fuera_de_alcance";

    /// <summary>Mismo texto para «no existe» y «no coincide»: no se puede saber cuál de las dos pasó.</summary>
    public const string MensajeNoEncontrada = "No encontramos un trámite con esos datos.";

    private const int MaxRadicado = 40;
    private const int MaxCedula = 20;

    public static IEndpointRouteBuilder MapSesionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        grupo.MapPost("/sesiones/reanudar", async (ReanudarRequest body, HttpContext http, SesionService sesiones, PiiHasher hasher, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Radicado) || string.IsNullOrWhiteSpace(body.Cedula)
                || body.Radicado.Length > MaxRadicado || body.Cedula.Length > MaxCedula)
                return Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "Indique el radicado y la cédula.", "datos_invalidos");

            var resultado = await sesiones.ReanudarAsync(body.Radicado, body.Cedula, hasher.HashIp(http.Connection.RemoteIpAddress?.ToString()), ct).ConfigureAwait(false);
            switch (resultado)
            {
                case ResultadoReanudar.Exito exito:
                    return Results.Ok(new ReanudarResponse(
                        exito.Sesion.Token, exito.Sesion.ExpiraEn, exito.Solicitud.SolicitudId, exito.Solicitud.CodigoPublico,
                        exito.Solicitud.Estado, exito.Solicitud.TipoTramiteCodigo));
                case ResultadoReanudar.Bloqueado bloqueado:
                    http.Response.Headers.RetryAfter = Math.Ceiling(bloqueado.EsperarHasta.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                    return Problema(StatusCodes.Status429TooManyRequests, "Demasiados intentos", "Espere unos minutos antes de volver a intentar.", CodigoBloqueado);
                default:
                    return Problema(StatusCodes.Status404NotFound, "No encontrado", MensajeNoEncontrada, CodigoNoEncontrada);
            }
        }).ConCaptcha();

        return app;
    }

    /// <summary>
    /// Filtro de las rutas <c>/solicitudes/{id}</c>: exige un token de sesión válido y que sea el de ESA solicitud. Con otra
    /// solicitud responde 403 y deja el intento en la auditoría; vencido, 401 <c>session_expired</c>; ausente o desconocido,
    /// 401 <c>session_invalid</c>.
    /// </summary>
    public static async ValueTask<object?> ConSesionDeLaSolicitud(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var http = contexto.HttpContext;
        var sesiones = http.RequestServices.GetRequiredService<SesionService>();
        var ct = http.RequestAborted;

        var cabecera = http.Request.Headers.Authorization.ToString();
        var token = cabecera.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? cabecera["Bearer ".Length..] : null;
        var sesion = await sesiones.ValidarAsync(token, ct).ConfigureAwait(false);

        if (sesion.Estado == EstadoSesion.Vencida)
            return Problema(StatusCodes.Status401Unauthorized, "Sesión vencida", "Su sesión venció. Puede reanudar el trámite con su radicado y su cédula.", CodigoSesionVencida);
        if (sesion.Estado != EstadoSesion.Valida)
            return Problema(StatusCodes.Status401Unauthorized, "Sesión no válida", "Falta una sesión válida.", CodigoSesionInvalida);

        var solicitudRuta = contexto.Arguments.OfType<Guid>().FirstOrDefault();
        if (solicitudRuta != sesion.SolicitudId)
        {
            var db = http.RequestServices.GetRequiredService<PersonaNaturalDb>();
            var hasher = http.RequestServices.GetRequiredService<PiiHasher>();
            db.Add(new AccesoPiiLog
            {
                Actor = "sesion",
                Accion = AccionFueraDeAlcance,
                SolicitudId = sesion.SolicitudId,
                IpHash = hasher.HashIp(http.Connection.RemoteIpAddress?.ToString()),
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Problema(StatusCodes.Status403Forbidden, "Sin acceso", "Esta sesión no abre ese trámite.", CodigoFueraDeAlcance);
        }

        return await siguiente(contexto).ConfigureAwait(false);
    }

    internal static IResult Problema(int estado, string titulo, string detalle, string codigo) =>
        Results.Problem(statusCode: estado, title: titulo, detail: detalle, extensions: new Dictionary<string, object?> { ["code"] = codigo });
}

public sealed record ReanudarRequest(string? Radicado, string? Cedula);

public sealed record ReanudarResponse(string Token, DateTimeOffset ExpiraEn, Guid SolicitudId, string CodigoPublico, string Estado, string TipoTramiteCodigo);

public sealed record SolicitudResponse(Guid SolicitudId, string CodigoPublico, string Estado, string TipoTramiteCodigo);
