using System.Globalization;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Sesiones;

namespace Flit.PersonaNatural.Api.Radicados;

/// <summary>Consulta pública de radicado (HU #14042): captcha y límite por IP del grupo, más el bloqueo por radicado.</summary>
public static class RadicadoEndpoints
{
    private const int MaxRadicado = 40;
    private const int MaxCedula = 20;
    private const int MaxPlaca = 12;

    public static IServiceCollection AddRadicadosPortal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<BloqueoPorRadicado>();
        services.AddScoped<IEstadoTramiteFuente, EstadoTramiteEspejo>();
        services.AddScoped<RadicadoConsultaService>();
        return services;
    }

    public static IEndpointRouteBuilder MapRadicadoEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(SesionEndpoints.Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        grupo.MapPost("/radicados/consulta", async (ConsultaRadicadoRequest body, HttpContext http, RadicadoConsultaService consultas, PiiHasher hasher, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Radicado) || string.IsNullOrWhiteSpace(body.Cedula)
                || body.Radicado.Length > MaxRadicado || body.Cedula.Length > MaxCedula || body.Placa?.Length > MaxPlaca)
                return SesionEndpoints.Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "Indique el radicado y la cédula.", "datos_invalidos");

            var resultado = await consultas.ConsultarAsync(body.Radicado, body.Cedula, body.Placa, hasher.HashIp(http.Connection.RemoteIpAddress?.ToString()), ct).ConfigureAwait(false);
            switch (resultado)
            {
                case ResultadoConsultaRadicado.Exito exito:
                    return Results.Ok(exito.Radicado);
                case ResultadoConsultaRadicado.Bloqueado bloqueado:
                    http.Response.Headers.RetryAfter = Math.Ceiling(bloqueado.EsperarHasta.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                    return SesionEndpoints.Problema(StatusCodes.Status429TooManyRequests, "Demasiados intentos", "Espere unos minutos antes de volver a intentar.", SesionEndpoints.CodigoBloqueado);
                default:
                    return SesionEndpoints.Problema(StatusCodes.Status404NotFound, "No encontrado", SesionEndpoints.MensajeNoEncontrada, SesionEndpoints.CodigoNoEncontrada);
            }
        }).ConCaptcha();

        return app;
    }
}

public sealed record ConsultaRadicadoRequest(string? Radicado, string? Cedula, string? Placa);
