using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Flit.Api.RateLimiting;

/// <summary>
/// Policy de límite de tasa <c>public-branding</c> (HU #12418 AC3) — la PRIMERA de
/// <c>Flit.Api</c> (delta-hechos #1: no existía <c>AddRateLimiter</c> en el repo). Fixed window
/// particionada por IP (primer hop de <c>X-Forwarded-For</c> que sella el borde, igual que
/// <c>HttpAuditContextAccessor</c> → <c>Connection.RemoteIpAddress</c>). Se aplica SOLO a
/// <c>GET /api/v1/public/branding</c> y <c>GET /api/v1/public/branding/logos/{logoId}</c>
/// (<c>RequireRateLimiting("public-branding")</c> en <c>PublicBrandingEndpoints</c>) — ninguna otra
/// ruta existente queda limitada.
/// </summary>
public static class PublicBrandingRateLimit
{
    public const string PolicyName = "public-branding";

    private const string ForwardedForHeader = "X-Forwarded-For";

    public static IServiceCollection AddPublicBrandingRateLimiter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(limiterOptions =>
        {
            // 429 (el default de ASP.NET Core es 503) sin cuerpo informativo (AC3): no revela
            // límite, ventana ni motivo al caller.
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiterOptions.OnRejected = (_, _) => ValueTask.CompletedTask;

            limiterOptions.AddPolicy(PolicyName, httpContext =>
            {
                var rateLimitOptions = httpContext.RequestServices
                    .GetRequiredService<IOptions<PublicBrandingOptions>>().Value.RateLimit;

                return RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, rateLimitOptions.PermitLimit),
                        Window = rateLimitOptions.Window <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : rateLimitOptions.Window,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });

        return services;
    }

    private static string ResolvePartitionKey(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue(ForwardedForHeader, out var forwarded))
        {
            var firstHop = forwarded.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (firstHop.Length > 0)
            {
                var candidate = firstHop[0].Trim();
                if (candidate.Length > 0)
                {
                    return candidate;
                }
            }
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
