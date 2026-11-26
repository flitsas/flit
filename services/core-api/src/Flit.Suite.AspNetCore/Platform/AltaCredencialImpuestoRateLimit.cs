using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Flit.Api.Platform;

/// <summary>
/// HU #14711 (plan §3.6; L-1/N-1) — límite de <c>POST …/products/diagnostico/credentials/impuesto/obtener</c>:
/// <see cref="PermisosPorVentana"/> llamadas por minuto por usuario (<c>sub</c>) y empresa (el <c>{tenantId}</c> de la ruta), ventana
/// fija y sin cola; el rechazo es un 429 sin cuerpo, como el rechazo común del host.
/// </summary>
/// <remarks>
/// No es una policy de <c>UseRateLimiter</c>: en los dos hosts ese middleware corre antes de <c>UseAuthentication</c>, así que la
/// partición no vería al usuario. Va como filtro del endpoint (<see cref="RequireAltaCredencialImpuestoRateLimit"/>), que corre
/// después de la autorización: una petición anónima o sin rol la rechaza la autorización y nunca consume cupo. Sin <c>sub</c> la
/// partición es <see cref="RateLimitPartition.GetNoLimiter{TKey}"/> (defensa en profundidad). Mismo criterio que
/// <c>ExternalClientQuotaMiddleware</c>.
/// </remarks>
public static class AltaCredencialImpuestoRateLimit
{
    public const int PermisosPorVentana = 5;

    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(1);

    /// <summary>Registra el limitador particionado (singleton: los contadores viven lo que el host).</summary>
    public static IServiceCollection AddAltaCredencialImpuestoRateLimiter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new LimitadorAltaCredencialImpuesto(PartitionedRateLimiter.Create<HttpContext, string>(Particion)));
        return services;
    }

    /// <summary>Aplica el límite al endpoint, detrás de la autorización.</summary>
    public static RouteHandlerBuilder RequireAltaCredencialImpuestoRateLimit(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var limitador = http.RequestServices.GetRequiredService<LimitadorAltaCredencialImpuesto>().Limitador;
            using var permiso = await limitador.AcquireAsync(http, 1, http.RequestAborted).ConfigureAwait(false);
            return permiso.IsAcquired ? await next(context).ConfigureAwait(false) : Results.StatusCode(StatusCodes.Status429TooManyRequests);
        });
    }

    /// <summary>Partición por <c>sub</c> + <c>{tenantId}</c>; sin usuario autenticado, sin límite (lo rechaza la autorización).</summary>
    public static RateLimitPartition<string> Particion(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var sub = http.User.Identity?.IsAuthenticated == true ? http.User.FindFirstValue("sub") : null;
        if (string.IsNullOrWhiteSpace(sub))
            return RateLimitPartition.GetNoLimiter("sin-usuario");

        var tenant = http.Request.RouteValues.TryGetValue("tenantId", out var t) ? t?.ToString() : null;
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{sub}:{tenant}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermisosPorVentana,
                Window = Ventana,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }
}

/// <summary>Contenedor del limitador del alta (tipo propio para no chocar con otros <see cref="PartitionedRateLimiter{TResource}"/>).</summary>
public sealed class LimitadorAltaCredencialImpuesto(PartitionedRateLimiter<HttpContext> limitador)
{
    public PartitionedRateLimiter<HttpContext> Limitador { get; } = limitador ?? throw new ArgumentNullException(nameof(limitador));
}
