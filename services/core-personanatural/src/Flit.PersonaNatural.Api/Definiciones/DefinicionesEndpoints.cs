using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Tramites.Grpc.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Definiciones;

/// <summary>
/// Definición del tipo de trámite (HU #14078, ADR-0075): <c>GET /tipos-tramite/{code}/definicion?organismoId=</c>. Pública, de
/// lectura y cacheada unos segundos: solo el límite por IP, sin captcha (la pide el front al elegir el tipo).
/// </summary>
public static class DefinicionesEndpoints
{
    private const int MaxCodigo = 80;

    /// <summary>
    /// Registra el servicio y el cliente de la definición. Reutiliza el cliente gRPC de la fachada que registra
    /// <see cref="CotizacionesEndpoints.AddCotizacionesPortal"/>: si core-api no está configurada, la definición responde 503.
    /// </summary>
    public static IServiceCollection AddDefinicionesPortal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMemoryCache();
        services.TryAddSingleton<IFachadaDefiniciones>(sp =>
            sp.GetService<PersonaNaturalService.PersonaNaturalServiceClient>() is { } cliente
                ? new FachadaDefinicionesGrpc(cliente)
                : new FachadaDefinicionesNoConfigurada());
        services.AddSingleton<DefinicionesService>();
        return services;
    }

    public static IEndpointRouteBuilder MapDefinicionesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(SesionEndpoints.Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        grupo.MapGet("/tipos-tramite/{code}/definicion", async (string code, string? organismoId, DefinicionesService definiciones, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > MaxCodigo)
                return SesionEndpoints.Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "Elija un tipo de trámite.", "datos_invalidos");
            if (!string.IsNullOrWhiteSpace(organismoId) && !Guid.TryParse(organismoId, out _))
                return SesionEndpoints.Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "El organismo no es válido.", "datos_invalidos");

            try
            {
                return Results.Ok(await definiciones.ObtenerAsync(code.Trim(), string.IsNullOrWhiteSpace(organismoId) ? null : organismoId.Trim(), ct).ConfigureAwait(false));
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        });

        return app;
    }
}
