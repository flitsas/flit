using System.Collections.Concurrent;
using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Infrastructure.Telemetry;
using Microsoft.Extensions.Options;

namespace Flit.Api.Middleware;

/// <summary>
/// Telemetría de uso server-side (HU-A Reportes 2.0, contrato §7). Tras la autenticación, si el
/// usuario está autenticado y tiene <c>tenant_id</c>, registra:
/// <list type="bullet">
///   <item><c>api_module_access</c> — request autenticado a un módulo de la API (mapeo ruta→módulo
///   del contrato), MUESTREADO a 1 evento por usuario+módulo+minuto con un caché en memoria acotado.</item>
///   <item><c>wizard_server_view</c> — <c>GET /api/v1/tramites/instances/{id}/wizard</c> (vista
///   server-driven del wizard), sin muestreo.</item>
/// </list>
/// TODO es fire-and-forget: <see cref="IUsageEventQueue.TryEnqueue"/> en memoria, nunca lanza,
/// presupuesto &lt; 5 ms (cero I/O síncrono). Cualquier fallo se traga: la telemetría JAMÁS
/// afecta al request.
/// </summary>
public sealed class UsageTelemetryMiddleware(
    RequestDelegate next,
    IUsageEventQueue queue,
    IOptions<AnalyticsTelemetryOptions> options)
{
    /// <summary>Tope de entradas del caché de muestreo; al superarlo se vacía (acotado y barato).</summary>
    internal const int MaxSampleCacheEntries = 10_000;

    // usuario+módulo → minuto epoch del último evento registrado (muestreo 1/min).
    private readonly ConcurrentDictionary<string, long> _sampleCache = new(StringComparer.Ordinal);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            TryRecord(context);
        }
        catch
        {
            // La telemetría nunca rompe el request (contrato §9).
        }

        await next(context);
    }

    private void TryRecord(HttpContext context)
    {
        if (!options.Value.Enabled)
            return;

        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
            return;

        if (!Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out var tenantId)
            || tenantId == Guid.Empty)
            return;

        Guid? userId = Guid.TryParse(user.FindFirstValue("sub"), out var sub) ? sub : null;
        var path = context.Request.Path;
        var now = DateTimeOffset.UtcNow;

        var module = ResolveModule(path);
        if (module is not null && ShouldSample(tenantId, userId, module, now))
        {
            queue.TryEnqueue(new UsageEventRecord(
                tenantId, userId, UsageEventTypes.ApiModuleAccess, module,
                StepKey: null, ProcedureInstanceId: null, DurationMs: null,
                MetadataJson: "{}", OccurredAt: now));
        }

        if (HttpMethods.IsGet(context.Request.Method) && TryExtractWizardInstanceId(path, out var instanceId))
        {
            queue.TryEnqueue(new UsageEventRecord(
                tenantId, userId, UsageEventTypes.WizardServerView, "tramites",
                StepKey: null, ProcedureInstanceId: instanceId, DurationMs: null,
                MetadataJson: "{}", OccurredAt: now));
        }
    }

    /// <summary>
    /// Mapeo ruta→módulo del contrato §7: <c>/api/v1/tramites/*→tramites</c>,
    /// <c>/api/v1/analytics/*→reportes</c>, <c>/api/v1/security/*→usuarios</c>,
    /// <c>/api/v1/admin/*→admin</c>, <c>/biometric-validations*→validaciones</c>.
    /// Las validaciones biométricas se evalúan primero (viven bajo /api/v1/tramites/…).
    /// Rutas fuera del mapa → sin evento.
    /// </summary>
    internal static string? ResolveModule(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
            return null;

        if (value.Contains("/biometric-validations", StringComparison.OrdinalIgnoreCase))
            return "validaciones";
        if (path.StartsWithSegments("/api/v1/tramites", StringComparison.OrdinalIgnoreCase))
            return "tramites";
        if (path.StartsWithSegments("/api/v1/analytics", StringComparison.OrdinalIgnoreCase))
            return "reportes";
        if (path.StartsWithSegments("/api/v1/security", StringComparison.OrdinalIgnoreCase))
            return "usuarios";
        if (path.StartsWithSegments("/api/v1/admin", StringComparison.OrdinalIgnoreCase))
            return "admin";
        return null;
    }

    /// <summary>¿Es <c>/api/v1/tramites/instances/{id}/wizard</c>? Extrae el id de instancia.</summary>
    internal static bool TryExtractWizardInstanceId(PathString path, out Guid instanceId)
    {
        instanceId = Guid.Empty;
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
            return false;

        const string prefix = "/api/v1/tramites/instances/";
        const string suffix = "/wizard";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var idSegment = value[prefix.Length..^suffix.Length];
        return Guid.TryParse(idSegment, out instanceId) && instanceId != Guid.Empty;
    }

    /// <summary>
    /// Muestreo de <c>api_module_access</c>: 1 evento por usuario+módulo+minuto. El caché es un
    /// diccionario en memoria acotado: al superar <see cref="MaxSampleCacheEntries"/> se limpia
    /// completo (más barato que LRU y suficiente: como mucho re-emite un evento por clave).
    /// </summary>
    private bool ShouldSample(Guid tenantId, Guid? userId, string module, DateTimeOffset now)
    {
        var minute = now.ToUnixTimeSeconds() / 60;
        var key = $"{tenantId:N}:{userId?.ToString("N") ?? "anon"}:{module}";

        if (_sampleCache.TryGetValue(key, out var lastMinute) && lastMinute == minute)
            return false;

        if (_sampleCache.Count >= MaxSampleCacheEntries)
            _sampleCache.Clear();

        _sampleCache[key] = minute;
        return true;
    }
}
