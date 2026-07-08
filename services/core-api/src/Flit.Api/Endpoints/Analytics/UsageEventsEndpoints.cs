using System.Security.Claims;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Infrastructure.Telemetry;
using Microsoft.Extensions.Options;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Endpoint batch de telemetría del frontend (HU-A Reportes 2.0, contrato §4.6):
/// <c>POST /api/v1/analytics/events</c>. Auth requerida; tenant y userId SIEMPRE del JWT
/// (claims <c>tenant_id</c> y <c>sub</c>), nunca del body. Máximo 50 eventos por lote
/// (más → 400 en español). Los <c>eventType</c> fuera de la taxonomía (§7) se descartan
/// silenciosamente. Responde 202 <c>{accepted: n}</c> y NUNCA 5xx por telemetría:
/// cualquier error inesperado se degrada a 202 con 0 aceptados.
/// </summary>
public static class UsageEventsEndpoints
{
    internal const int MaxBatchSize = 50;

    /// <summary>Longitud máxima serializada del metadata aceptado (jsonb); más largo → se descarta a {}.</summary>
    private const int MaxMetadataLength = 2_000;

    public static IEndpointRouteBuilder MapUsageEventsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/v1/analytics/events", PostEventsAsync)
            .RequireAuthorization()
            .WithTags("Analytics · Telemetría")
            .WithName("AnalyticsUsageEventsBatch")
            .WithSummary("Recibe un lote (máx. 50) de eventos de telemetría de uso del frontend")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static IResult PostEventsAsync(
        HttpContext httpContext,
        UsageEventsBatchRequest? request,
        IUsageEventQueue queue,
        IOptions<AnalyticsTelemetryOptions> options)
    {
        try
        {
            var events = request?.Events;
            if (events is null || events.Count == 0)
                return Results.Accepted(value: new { accepted = 0 });

            if (events.Count > MaxBatchSize)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                    detail: $"El lote supera el máximo de {MaxBatchSize} eventos de telemetría.");

            // Tenant y userId SIEMPRE del JWT — el body no puede suplantarlos (§4.6).
            var user = httpContext.User;
            if (!Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out var tenantId)
                || tenantId == Guid.Empty)
            {
                // Sin tenant no hay a quién atribuir la telemetría: se descarta sin error (best-effort).
                return Results.Accepted(value: new { accepted = 0 });
            }

            Guid? userId = Guid.TryParse(user.FindFirstValue("sub"), out var sub) ? sub : null;

            if (!options.Value.Enabled)
                return Results.Accepted(value: new { accepted = 0 });

            var accepted = 0;
            var now = DateTimeOffset.UtcNow;
            foreach (var evt in events)
            {
                if (evt is null || !UsageEventTypes.IsKnown(evt.EventType))
                    continue; // fuera de taxonomía → descarte silencioso

                var record = new UsageEventRecord(
                    tenantId,
                    userId,
                    evt.EventType!,
                    Truncate(evt.Module, 40),
                    Truncate(evt.StepKey, 40),
                    evt.ProcedureInstanceId,
                    evt.DurationMs is < 0 ? null : evt.DurationMs,
                    NormalizeMetadata(evt.Metadata),
                    evt.OccurredAt ?? now);

                if (queue.TryEnqueue(record))
                    accepted++;
            }

            return Results.Accepted(value: new { accepted });
        }
        catch
        {
            // NUNCA 5xx por telemetría (§4.6): se degrada a "nada aceptado".
            return Results.Accepted(value: new { accepted = 0 });
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    /// <summary>Metadata → jsonb: solo objetos JSON razonablemente pequeños; el resto colapsa a {}.</summary>
    private static string NormalizeMetadata(JsonElement? metadata)
    {
        if (metadata is not { ValueKind: JsonValueKind.Object } element)
            return "{}";
        var raw = element.GetRawText();
        return raw.Length <= MaxMetadataLength ? raw : "{}";
    }
}

/// <summary>Lote de eventos de telemetría del frontend (contrato §4.6). Sin tenant/userId: van en el JWT.</summary>
public sealed record UsageEventsBatchRequest(IReadOnlyList<UsageEventInput?>? Events);

/// <summary>Evento individual del lote. <c>Metadata</c> es jsonb SIN PII (nombres, documentos, emails…).</summary>
public sealed record UsageEventInput(
    string? EventType,
    string? Module,
    string? StepKey,
    Guid? ProcedureInstanceId,
    int? DurationMs,
    DateTimeOffset? OccurredAt,
    JsonElement? Metadata);
