using System.Globalization;
using Flit.Notificaciones.Grpc.V1;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Cotizaciones;

/// <summary>
/// Catálogo y cotización públicos del portal (HU #14043, ADR-0075): organismos, tipos de trámite, cotización de 2 líneas y
/// envío por correo. Sin sesión; límite por IP (grupo) y captcha en los POST. El BFF no tiene precios: llama a la fachada de
/// core-api, que es el único cálculo.
/// </summary>
public static class CotizacionesEndpoints
{
    public const string Base = SesionEndpoints.Base;
    public const string CabeceraIdempotencia = "Idempotency-Key";
    public const string CodigoLimiteCorreo = "correo_rate_limited";
    public const string CodigoIdempotenciaRequerida = "idempotency_key_requerida";
    public const string CodigoEnvioFallido = "envio_fallido";
    private const int MaxAdicionales = 4;

    /// <summary>Registra el servicio, la caché y, si hay direcciones configuradas, los clientes gRPC de core-api y notificaciones.</summary>
    public static IServiceCollection AddCotizacionesPortal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<CotizacionesOptions>(configuration.GetSection(CotizacionesOptions.SectionName));
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CotizacionesService>();

        var opciones = configuration.GetSection(CotizacionesOptions.SectionName).Get<CotizacionesOptions>() ?? new CotizacionesOptions();
        if (Uri.TryCreate(opciones.CoreApiAddress, UriKind.Absolute, out var coreApi))
        {
            services.AddFlitGrpcClient<PersonaNaturalService.PersonaNaturalServiceClient>(configuration, coreApi, FachadaTramitesGrpc.Scope);
            services.TryAddSingleton<IFachadaTramites, FachadaTramitesGrpc>();
        }
        else
        {
            services.TryAddSingleton<IFachadaTramites, FachadaTramitesNoConfigurada>();
        }

        if (Uri.TryCreate(opciones.NotificacionesAddress, UriKind.Absolute, out var notificaciones))
        {
            services.AddFlitGrpcClient<NotificacionesService.NotificacionesServiceClient>(configuration, notificaciones, EnviadorCotizacionGrpc.Scope);
            services.TryAddSingleton<IEnviadorCotizacion, EnviadorCotizacionGrpc>();
        }
        else
        {
            services.TryAddSingleton<IEnviadorCotizacion, EnviadorCotizacionNoConfigurado>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapCotizacionesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        // El catálogo es de lectura y va cacheado: sin captcha, solo con el límite por IP (la landing lo pide al abrir).
        grupo.MapGet("/organismos", (CotizacionesService s, CancellationToken ct) => Atender(async () => Results.Ok(await s.OrganismosAsync(ct).ConfigureAwait(false))));

        grupo.MapGet("/tipos-tramite", (string? organismoId, CotizacionesService s, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(organismoId) && !Guid.TryParse(organismoId, out _))
                return Task.FromResult(Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "El organismo no es válido.", "datos_invalidos"));
            return Atender(async () => Results.Ok(await s.TiposAsync(string.IsNullOrWhiteSpace(organismoId) ? null : organismoId, ct).ConfigureAwait(false)));
        });

        grupo.MapPost("/cotizaciones", (CotizarSolicitud body, CotizacionesService s, CancellationToken ct) =>
        {
            if (!Validar(body?.OrganismoId, body?.TipoCodigo, body?.Adicionales, out var detalle))
                return Task.FromResult(Problema(StatusCodes.Status400BadRequest, "Datos inválidos", detalle, "datos_invalidos"));
            return Atender(async () => Results.Ok(await s.CotizarAsync(body!.TipoCodigo!.Trim(), body.OrganismoId!.Trim(), body.Adicionales ?? [], ct).ConfigureAwait(false)));
        }).ConCaptcha();

        grupo.MapPost("/cotizaciones/enviar-correo", (EnviarCotizacionSolicitud body, HttpContext http, CotizacionesService s, CancellationToken ct) =>
        {
            var clave = http.Request.Headers[CabeceraIdempotencia].ToString();
            if (string.IsNullOrWhiteSpace(clave) || clave.Length > 100)
                return Task.FromResult(Problema(StatusCodes.Status400BadRequest, "Falta la clave", "Envíe una Idempotency-Key de hasta 100 caracteres.", CodigoIdempotenciaRequerida));
            if (!Validar(body?.OrganismoId, body?.TipoCodigo, body?.Adicionales, out var detalle))
                return Task.FromResult(Problema(StatusCodes.Status400BadRequest, "Datos inválidos", detalle, "datos_invalidos"));
            if (!CotizacionesService.CorreoValido(body!.Correo))
                return Task.FromResult(Problema(StatusCodes.Status400BadRequest, "Correo inválido", "Escriba un correo válido.", "correo_invalido"));

            return Atender(async () =>
            {
                var resultado = await s.EnviarPorCorreoAsync(
                    body.Correo!.Trim(), clave, body.TipoCodigo!.Trim(), body.OrganismoId!.Trim(), body.Adicionales ?? [], ct).ConfigureAwait(false);
                switch (resultado)
                {
                    case ResultadoEnvioCotizacion.Enviado:
                        return Results.Ok(new EnvioResponse(true, false));
                    case ResultadoEnvioCotizacion.Repetido:
                        return Results.Ok(new EnvioResponse(true, true));
                    case ResultadoEnvioCotizacion.LimiteAlcanzado:
                        http.Response.Headers.RetryAfter = (60 * 60).ToString(CultureInfo.InvariantCulture);
                        return Problema(StatusCodes.Status429TooManyRequests, "Demasiados envíos", "Ese correo ya recibió varias cotizaciones. Intente más tarde.", CodigoLimiteCorreo);
                    default:
                        return Problema(StatusCodes.Status502BadGateway, "No se pudo enviar", "No pudimos enviar el correo. Intente de nuevo.", CodigoEnvioFallido);
                }
            });
        }).ConCaptcha();

        return app;
    }

    private static bool Validar(string? organismoId, string? tipoCodigo, string[]? adicionales, out string detalle)
    {
        detalle = string.Empty;
        if (!Guid.TryParse(organismoId, out _))
            detalle = "Elija un organismo de tránsito.";
        else if (string.IsNullOrWhiteSpace(tipoCodigo) || tipoCodigo.Length > 80)
            detalle = "Elija un tipo de trámite.";
        else if (adicionales is { Length: > MaxAdicionales } || adicionales?.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 40) == true)
            detalle = "Los conceptos adicionales no son válidos.";
        return detalle.Length == 0;
    }

    /// <summary>Ejecuta la acción y traduce los errores de la fachada a <c>problem+json</c> con el código estable de FLIT 2.</summary>
    private static async Task<IResult> Atender(Func<Task<IResult>> accion)
    {
        try
        {
            return await accion().ConfigureAwait(false);
        }
        catch (FachadaException ex)
        {
            return Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
        }
    }

    private static IResult Problema(int estado, string titulo, string detalle, string codigo) => SesionEndpoints.Problema(estado, titulo, detalle, codigo);
}

public sealed record CotizarSolicitud(string? OrganismoId, string? TipoCodigo, string[]? Adicionales);

public sealed record EnviarCotizacionSolicitud(string? OrganismoId, string? TipoCodigo, string[]? Adicionales, string? Correo);

public sealed record EnvioResponse(bool Enviado, bool Repetido);
