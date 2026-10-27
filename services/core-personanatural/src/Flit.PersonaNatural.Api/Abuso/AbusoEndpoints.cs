using System.Globalization;
using System.Threading.RateLimiting;
using Flit.PersonaNatural.Api.Sesiones;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Abuso;

/// <summary>Piezas HTTP de la protección anti-abuso (HU #14036): límite por IP, captcha y tope de consultas.</summary>
public static class AbusoEndpoints
{
    public const string PoliticaPorIp = "por-ip";
    public const string CabeceraCaptcha = "X-Captcha-Token";
    public const string CodigoCaptcha = "captcha_required";
    public const string CodigoLimitada = "rate_limited";
    public const string CodigoCedulaLimitada = "cedula_rate_limited";
    public const string CodigoSinConsultas = "consultas_no_disponibles";

    public static IServiceCollection AddAbusoPortal(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AbusoOptions>(configuration.GetSection(AbusoOptions.SectionName));
        services.Configure<CaptchaOptions>(configuration.GetSection(CaptchaOptions.SectionName));
        services.AddScoped<CuotaService>();

        var abuso = configuration.GetSection(AbusoOptions.SectionName).Get<AbusoOptions>() ?? new AbusoOptions();
        var captcha = configuration.GetSection(CaptchaOptions.SectionName).Get<CaptchaOptions>() ?? new CaptchaOptions();
        if (string.Equals(captcha.Proveedor, CaptchaOptions.Deshabilitado, StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddSingleton<IProveedorCaptcha, ProveedorCaptchaDeshabilitado>();
        }
        else
        {
            services.AddHttpClient(nameof(ProveedorCaptchaHttp), c => c.Timeout = TimeSpan.FromSeconds(5));
            services.TryAddTransient<IProveedorCaptcha>(sp => ActivatorUtilities.CreateInstance<ProveedorCaptchaHttp>(
                sp, sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ProveedorCaptchaHttp))));
        }

        // Ventana fija por IP, en memoria de cada réplica: frena el abuso por volumen. El tope exacto entre réplicas lo dan
        // los contadores de la base (cédula y global).
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(PoliticaPorIp, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = abuso.LimiteIpPorMinuto, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.OnRejected = async (contexto, ct) =>
            {
                var espera = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? Math.Ceiling(retry.TotalSeconds) : 60;
                contexto.HttpContext.Response.Headers.RetryAfter = espera.ToString(CultureInfo.InvariantCulture);
                await SesionEndpoints.Problema(StatusCodes.Status429TooManyRequests, "Demasiadas solicitudes", "Espere un momento antes de volver a intentar.", CodigoLimitada)
                    .ExecuteAsync(contexto.HttpContext).ConfigureAwait(false);
            };
        });

        if (abuso.ConfiarEnXForwardedFor)
        {
            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });
        }

        return services;
    }

    /// <summary>Exige un captcha resuelto (cabecera <c>X-Captcha-Token</c>) antes de ejecutar el endpoint.</summary>
    public static RouteHandlerBuilder ConCaptcha(this RouteHandlerBuilder builder) => builder.AddEndpointFilter(async (contexto, siguiente) =>
    {
        var http = contexto.HttpContext;
        var token = http.Request.Headers[CabeceraCaptcha].ToString();
        var proveedor = http.RequestServices.GetRequiredService<IProveedorCaptcha>();
        if (string.IsNullOrWhiteSpace(token) || !await proveedor.VerificarAsync(token, http.Connection.RemoteIpAddress?.ToString(), http.RequestAborted).ConfigureAwait(false))
            return SesionEndpoints.Problema(StatusCodes.Status400BadRequest, "Captcha requerido", "Resuelva el captcha para continuar.", CodigoCaptcha);
        return await siguiente(contexto).ConfigureAwait(false);
    });

    /// <summary>
    /// Cuenta la consulta contra los topes por cédula y global antes de ejecutarla. <paramref name="cedula"/> saca del request
    /// la cédula consultada; sin ella el filtro no cuenta nada.
    /// </summary>
    public static RouteHandlerBuilder ConLimiteDeConsultas(this RouteHandlerBuilder builder, Func<EndpointFilterInvocationContext, string?> cedula) =>
        builder.AddEndpointFilter(async (contexto, siguiente) =>
        {
            var documento = cedula(contexto);
            if (string.IsNullOrWhiteSpace(documento))
                return await siguiente(contexto).ConfigureAwait(false);

            var http = contexto.HttpContext;
            var hasher = http.RequestServices.GetRequiredService<PiiHasher>();
            var cuota = http.RequestServices.GetRequiredService<CuotaService>();
            var resultado = await cuota.ReservarConsultaAsync(documento, hasher.HashIp(http.Connection.RemoteIpAddress?.ToString()), http.RequestAborted).ConfigureAwait(false);
            switch (resultado)
            {
                case ResultadoCuota.CedulaExcedida:
                    http.Response.Headers.RetryAfter = "3600";
                    return SesionEndpoints.Problema(StatusCodes.Status429TooManyRequests, "Demasiadas consultas", "Se alcanzó el máximo de consultas por hora para este documento.", CodigoCedulaLimitada);
                case ResultadoCuota.TopeGlobal:
                    return SesionEndpoints.Problema(StatusCodes.Status503ServiceUnavailable, "Consultas no disponibles", "Las consultas están pausadas por hoy. Intente de nuevo mañana.", CodigoSinConsultas);
                default:
                    return await siguiente(contexto).ConfigureAwait(false);
            }
        });
}
