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

        MediosPagoRegistro.Registrar(services, coreApi is not null);

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

        // El catálogo es de lectura: sin captcha, solo con el límite por IP (la landing lo pide al abrir). HU #14391: lo que se
        // ofrece depende de los precios, así que no se cachea (ni aquí ni en el navegador): un precio nuevo se ve de inmediato.
        grupo.MapGet("/organismos", (HttpContext http, CotizacionesService s, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Atender(async () => Results.Ok(await s.OrganismosAsync(ct).ConfigureAwait(false)));
        });

        grupo.MapGet("/tipos-tramite", (string? organismoId, HttpContext http, CotizacionesService s, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!string.IsNullOrWhiteSpace(organismoId) && !Guid.TryParse(organismoId, out _))
                return Task.FromResult(Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "El organismo no es válido.", "datos_invalidos"));
            return Atender(async () => Results.Ok(await s.TiposAsync(string.IsNullOrWhiteSpace(organismoId) ? null : organismoId, ct).ConfigureAwait(false)));
        });

        // HU #14395: medios de pago que la web ofrece para un tipo y un organismo (sin PIN: es del punto). Sin caché, como el catálogo:
        // una bandera o un switch recién guardados se ven en la siguiente consulta.
        grupo.MapGet("/medios-pago", (string? organismoId, string? tipoCodigo, HttpContext http, IFachadaMediosPago medios, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!Validar(organismoId, tipoCodigo, null, out var detalle))
                return Task.FromResult(Problema(StatusCodes.Status400BadRequest, "Datos inválidos", detalle, "datos_invalidos"));
            return Atender(async () => Results.Ok(await medios.ListarMediosPagoAsync(tipoCodigo!.Trim(), organismoId!.Trim(), null, ct).ConfigureAwait(false)));
        });

        grupo.MapPost("/cotizaciones", (CotizarSolicitud body, CotizacionesService s, IFachadaMediosPago medios, CancellationToken ct) =>
        {
            if (!Validar(body?.OrganismoId, body?.TipoCodigo, body?.Adicionales, out var detalle))
                return Task.FromResult(Problema(StatusCodes.Status400BadRequest, "Datos inválidos", detalle, "datos_invalidos"));
            // HU #14395: con medio se cotizan solo los conceptos encendidos en él; sin medio, la respuesta es la de siempre.
            if (body!.Medio is { } medio)
            {
                var m = medio.Trim().ToUpperInvariant();
                if (m is not ("PASARELA" or "PIN"))
                    return Task.FromResult(Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "El medio de pago no es válido.", "datos_invalidos"));
                return Atender(async () => Results.Ok(await medios.CotizarConMedioAsync(body.TipoCodigo!.Trim(), body.OrganismoId!.Trim(), body.Adicionales ?? [], m, null, ct).ConfigureAwait(false)));
            }

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

public sealed record CotizarSolicitud(string? OrganismoId, string? TipoCodigo, string[]? Adicionales, string? Medio = null);

public sealed record EnviarCotizacionSolicitud(string? OrganismoId, string? TipoCodigo, string[]? Adicionales, string? Correo);

public sealed record EnvioResponse(bool Enviado, bool Repetido);
