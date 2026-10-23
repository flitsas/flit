using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Flit.Api.RateLimiting;

/// <summary>
/// HU #13289 (Feature #13281 B, Épica #13202) — policy <c>manual-capture</c> de los endpoints públicos de captura manual
/// (<c>/api/v1/public/manual-capture/*</c>): ventana fija por IP del cliente (la misma clave que <c>public-branding</c> y
/// <c>external-token</c>: primer hop de <c>X-Forwarded-For</c> o la conexión), <c>ManualCapture:RequestsPerIpPerMinute</c>
/// (60 por defecto; una captura honesta usa ~7 peticiones). Al rechazar responde 429 sin cuerpo (no revela límite ni ventana).
/// <para>
/// Solo por IP: ASP.NET Core aplica UNA policy por endpoint, así que no se encadena un segundo límite por token. Un token de
/// 256 bits no se adivina; el límite por IP frena la enumeración y la fuerza bruta sobre muchos tokens.
/// </para>
/// </summary>
public static class ManualCaptureRateLimit
{
    public const string PolicyName = "manual-capture";

    private const int DefaultRequestsPerIpPerMinute = 60;

    public static IServiceCollection AddManualCaptureRateLimiter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiterOptions.AddPolicy(PolicyName, httpContext =>
            {
                // Se lee al crear el limitador de cada IP (no al registrar): respeta la configuración efectiva del host.
                var permitLimit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue("ManualCapture:RequestsPerIpPerMinute", DefaultRequestsPerIpPerMinute);

                return RateLimitPartition.GetFixedWindowLimiter(
                    PublicBrandingRateLimit.ResolvePartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, permitLimit),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });

        return services;
    }
}
