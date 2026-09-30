using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Flit.Admin.Domain.Integrations;
using Flit.Api.Authorization;
using Flit.Api.RateLimiting;
using Flit.Infrastructure.Security;

namespace Flit.Api.Middleware;

/// <summary>
/// HU #13086 (Feature #13067, Épica #12737) — una fila en <c>integrations.external_access_log</c> por cada
/// solicitud a <c>/api/v1/external/*</c>, atendida o rechazada (Ley 1581). Va ANTES de <c>UseRateLimiter</c>
/// para registrar también los 429 del canje del pase y de la cuota por cliente, y los 401/403 de la
/// autorización. Los endpoints aportan lo que entregaron vía <see cref="ExternalAccessDetails"/>.
/// <para>Nunca guarda cuerpos, datos personales, secretos ni pases (AC3). Si la escritura falla, la
/// solicitud no se ve afectada: la respuesta ya salió; queda un error en el log con endpoint y código.</para>
/// </summary>
public sealed partial class ExternalAccessLogMiddleware(RequestDelegate next, ILogger<ExternalAccessLogMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IExternalAccessLogRepository repository, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (!context.Request.Path.StartsWithSegments(ExternalClientAuthorization.RoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var details = new ExternalAccessDetails();
        context.Features.Set(details);
        var occurredAt = timeProvider.GetUtcNow();
        var watch = Stopwatch.StartNew();
        var status = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context).ConfigureAwait(false);
            status = context.Response.StatusCode;
        }
        finally
        {
            watch.Stop();
            var entry = new ExternalAccessLogEntry(
                context.User.FindFirst(ExternalJwtTokenIssuer.ClientIdClaim)?.Value ?? details.RequestedClientId,
                EndpointName(context),
                Truncate(context.TraceIdentifier, 64),
                IPAddress.TryParse(PublicBrandingRateLimit.ResolvePartitionKey(context), out var ip) ? ip : null,
                details.SyncVersionFrom,
                details.SyncVersionTo,
                details.ItemsCount,
                details.TenantIds,
                details.PiiUnmasked,
                status,
                (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds),
                occurredAt);
            try
            {
                // Sin el token de la petición: un cliente que corta la conexión igual queda registrado.
                await repository.AddAsync(entry, CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // La bitácora no puede tumbar una respuesta ya enviada.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogWriteFailed(logger, ex, entry.Endpoint, status);
            }
        }
    }

    /// <summary>Nombre estable del endpoint para la bitácora (no la ruta: esa lleva ids).</summary>
    private static string EndpointName(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName switch
        {
            "ExternalAuthToken" => "token",
            "ExternalTramitesSync" => "tramites.sync",
            "ExternalTramiteAdjuntoUrl" => "tramites.adjunto-url",
            _ => "otro",
        };

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Error,
        Message = "No se pudo escribir la bitácora de acceso externo ({Endpoint}, {Status}).")]
    private static partial void LogWriteFailed(ILogger logger, Exception ex, string endpoint, int status);
}

/// <summary>
/// HU #13086 — lo que un endpoint externo entregó, para la bitácora. Lo crea <see cref="ExternalAccessLogMiddleware"/>
/// y lo completa el endpoint con <c>HttpContext.Features.Get&lt;ExternalAccessDetails&gt;()</c>.
/// </summary>
public sealed partial class ExternalAccessDetails
{
    public long? SyncVersionFrom { get; private set; }

    public long? SyncVersionTo { get; private set; }

    public int? ItemsCount { get; private set; }

    public IReadOnlyList<Guid>? TenantIds { get; private set; }

    public bool PiiUnmasked { get; private set; }

    /// <summary>Canje del pase: el client_id solicitado, solo si tiene el formato válido (nunca texto libre).</summary>
    public string? RequestedClientId { get; private set; }

    public void SetPage(IReadOnlyList<long> syncVersions, IEnumerable<Guid> tenantIds, bool piiUnmasked)
    {
        ArgumentNullException.ThrowIfNull(syncVersions);
        ArgumentNullException.ThrowIfNull(tenantIds);
        ItemsCount = syncVersions.Count;
        SyncVersionFrom = syncVersions.Count > 0 ? syncVersions[0] : null;
        SyncVersionTo = syncVersions.Count > 0 ? syncVersions[^1] : null;
        TenantIds = tenantIds.Distinct().ToList();
        PiiUnmasked = piiUnmasked;
    }

    public void SetRequestedClientId(string? clientId) =>
        RequestedClientId = clientId is not null && ClientIdFormat().IsMatch(clientId) ? clientId : null;

    /// <summary>Mismo formato que <c>ck_external_clients_client_id_formato</c> (DDL 125).</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdFormat();
}
