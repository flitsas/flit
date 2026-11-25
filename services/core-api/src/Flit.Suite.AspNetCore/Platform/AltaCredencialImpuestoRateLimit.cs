using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Flit.Api.Platform;

/// <summary>
/// HU #14711 (plan §3.6) — límite de <c>POST …/products/diagnostico/credentials/impuesto/obtener</c>:
/// <see cref="PermisosPorVentana"/> llamadas por minuto y por empresa (la del <c>{tenantId}</c> de la ruta), ventana fija y sin
/// cola. Con el rechazo común del host (429 sin cuerpo).
/// </summary>
public static class AltaCredencialImpuestoRateLimit
{
    public const string PolicyName = "alta-credencial-impuesto";

    public const int PermisosPorVentana = 5;

    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddAltaCredencialImpuestoRateLimiter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(PolicyName, http => RateLimitPartition.GetFixedWindowLimiter(
                "tenant:" + (http.Request.RouteValues.TryGetValue("tenantId", out var tenant) ? tenant?.ToString() : null),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermisosPorVentana,
                    Window = Ventana,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });
        return services;
    }
}
