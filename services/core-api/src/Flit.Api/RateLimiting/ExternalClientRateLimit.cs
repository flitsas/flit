using System.Globalization;
using System.Threading.RateLimiting;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Flit.Api.RateLimiting;

/// <summary>
/// HU #13087 (Épica #12737) — policy <c>external-token</c>: 10 peticiones de pase por minuto por IP
/// (<c>ExternalClients:TokenRequestsPerIpPerMinute</c>), ventana fija. Al rechazar responde 429 con
/// <c>Retry-After</c> y problem+json, pero SOLO bajo <c>/api/v1/external</c>: el resto de policies
/// (<c>public-branding</c>) sigue sin revelar límite ni ventana.
/// </summary>
public static class ExternalClientRateLimit
{
    public const string TokenPolicyName = "external-token";

    private const int DefaultTokenRequestsPerMinute = 10;

    /// <summary>Va DESPUÉS de <see cref="PublicBrandingRateLimit.AddPublicBrandingRateLimiter"/>: fija el <c>OnRejected</c> común.</summary>
    public static IServiceCollection AddExternalClientRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var permitLimit = configuration.GetValue("ExternalClients:TokenRequestsPerIpPerMinute", DefaultTokenRequestsPerMinute);

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiterOptions.OnRejected = OnRejectedAsync;

            limiterOptions.AddPolicy(TokenPolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PublicBrandingRateLimit.ResolvePartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, permitLimit),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        });

        return services;
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (!http.Request.Path.StartsWithSegments(ExternalClientAuthorization.RoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return; // public-branding: 429 sin cuerpo ni cabeceras (HU #12418 AC3).
        }

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
            : 60;
        http.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        await ExternalProblem.WriteAsync(http, StatusCodes.Status429TooManyRequests, "rate_limited",
            "Demasiadas solicitudes. Reintente después de Retry-After.", cancellationToken).ConfigureAwait(false);
    }
}
