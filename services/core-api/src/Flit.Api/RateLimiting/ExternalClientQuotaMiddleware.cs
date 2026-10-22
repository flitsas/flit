using System.Globalization;
using System.Threading.RateLimiting;
using Flit.Api.Authorization;
using Flit.Infrastructure.Security;

namespace Flit.Api.RateLimiting;

/// <summary>
/// HU #13085 (Feature #13067, Épica #12737) — cuota por cliente externo: <c>ExternalClients:RequestsPerClientPerMinute</c>
/// (120 por defecto) en ventana deslizante de 1 minuto por <c>client_id</c>. Al superarla responde 429 con
/// <c>Retry-After</c> en problem+json (<c>rate_limited</c>, contrato v3.1 §3).
/// <para>No es una policy de <c>UseRateLimiter</c> (a diferencia de <c>external-token</c>) porque ese middleware corre
/// ANTES de la autenticación: el <c>client_id</c> solo es fiable después de validar el pase. Leerlo de un pase sin
/// validar dejaría a un tercero agotar la cuota de Flito con pases falsos. Por eso va tras <c>UseAuthorization</c>
/// y solo cuenta peticiones con pase válido y permiso; los 401/403 no consumen cuota.</para>
/// </summary>
public sealed class ExternalClientQuotaMiddleware : IDisposable
{
    public const string ConfigKey = "ExternalClients:RequestsPerClientPerMinute";
    public const int DefaultRequestsPerMinute = 120;

    /// <summary>6 segmentos de 10 s: la cuota se recupera de a poco, no de golpe cada minuto.</summary>
    private const int SegmentsPerWindow = 6;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly RequestDelegate _next;
    private readonly PartitionedRateLimiter<string> _limiter;

    public ExternalClientQuotaMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _next = next;
        var permitLimit = Math.Max(1, configuration.GetValue(ConfigKey, DefaultRequestsPerMinute));
        _limiter = PartitionedRateLimiter.Create<string, string>(clientId =>
            RateLimitPartition.GetSlidingWindowLimiter(clientId, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = Window,
                SegmentsPerWindow = SegmentsPerWindow,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var clientId = context.Request.Path.StartsWithSegments(ExternalClientAuthorization.RoutePrefix, StringComparison.OrdinalIgnoreCase)
            ? context.User.FindFirst(ExternalJwtTokenIssuer.ClientIdClaim)?.Value
            : null;
        if (string.IsNullOrEmpty(clientId))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        using var lease = _limiter.AttemptAcquire(clientId);
        if (lease.IsAcquired)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? wait
            : Window / SegmentsPerWindow;
        context.Response.Headers.RetryAfter =
            Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        await ExternalProblem.WriteAsync(context, StatusCodes.Status429TooManyRequests, "rate_limited",
            "Demasiadas solicitudes. Reintente después de Retry-After.", context.RequestAborted).ConfigureAwait(false);
    }

    public void Dispose() => _limiter.Dispose();
}
