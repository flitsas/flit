using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Tramites.Grpc.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Configuracion;

/// <summary>
/// Contenido público del portal (HU #14073, ADR-0075): <c>GET /api/v1/persona-natural/configuracion</c> y la imagen de cada
/// banner. Públicos, de lectura y con caché de 60 s: solo el límite por IP, sin captcha (la landing los pide al abrir).
/// </summary>
public static class ConfiguracionEndpoints
{
    /// <summary>Largo máximo del ETag (sha256 en hex) que se acepta; un valor más largo se ignora.</summary>
    private const int MaxIfNoneMatch = 100;

    /// <summary>
    /// Registra el servicio. Reutiliza el cliente gRPC de la fachada que registra
    /// <see cref="CotizacionesEndpoints.AddCotizacionesPortal"/>: si core-api no está configurada, el contenido responde 503.
    /// </summary>
    public static IServiceCollection AddConfiguracionPortal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMemoryCache();
        services.TryAddSingleton<IFachadaConfiguracion>(sp =>
            sp.GetService<PersonaNaturalService.PersonaNaturalServiceClient>() is { } cliente
                ? new FachadaConfiguracionGrpc(cliente)
                : new FachadaConfiguracionNoConfigurada());
        services.AddSingleton<ConfiguracionService>();
        return services;
    }

    public static IEndpointRouteBuilder MapConfiguracionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(SesionEndpoints.Base + "/configuracion").AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        grupo.MapGet(string.Empty, async (ConfiguracionService s, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var configuracion = await s.ObtenerAsync(ct).ConfigureAwait(false);
                http.Response.Headers.CacheControl = "public, max-age=60";
                return Results.Ok(configuracion);
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        });

        grupo.MapGet("/banners/{id}/imagen", async (string id, ConfiguracionService s, HttpContext http, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var banner))
                return SesionEndpoints.Problema(StatusCodes.Status404NotFound, "No encontrado", "La imagen no existe.", "no_encontrado");

            var etag = http.Request.Headers.IfNoneMatch.ToString().Trim().Trim('"');
            try
            {
                var imagen = await s.ImagenAsync(banner.ToString(), etag.Length is > 0 and <= MaxIfNoneMatch ? etag : null, ct).ConfigureAwait(false);
                if (imagen is null)
                    return SesionEndpoints.Problema(StatusCodes.Status404NotFound, "No encontrado", "La imagen no existe.", "no_encontrado");
                http.Response.Headers.CacheControl = "public, max-age=60";
                http.Response.Headers.ETag = $"\"{imagen.Sha256}\"";
                return imagen.NoModificado ? Results.StatusCode(StatusCodes.Status304NotModified) : Results.Bytes(imagen.Contenido, imagen.ContentType);
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        });

        return app;
    }
}
