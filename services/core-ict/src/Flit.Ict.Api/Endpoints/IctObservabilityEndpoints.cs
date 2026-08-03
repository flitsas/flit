using Flit.Ict.Api.Authorization;
using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// Observabilidad ICT para el submódulo frontend (<c>/api/v1/ict/logs</c>, <c>/api/v1/ict/alerts</c>).
/// El Gateway aplica JwtRequired (JWT de plataforma); aquí se verifica el permiso ict.logs.read.
/// </summary>
public static class IctObservabilityEndpoints
{
    public static IEndpointRouteBuilder MapIctObservabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ict");

        group.MapGet("/logs", async (
            HttpContext context,
            IIntegrationLogQuery query,
            string? logType,
            Guid? correlationId,
            string? search,
            DateTime? from,
            DateTime? to,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasIctLogsAccess)
            {
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            // Superadmin ve todos los tenants; el resto, solo el suyo.
            var tenantScope = access.IsSuperAdmin ? null : access.TenantId;
            var result = await query.QueryAsync(
                new LogQueryFilter(tenantScope, logType, correlationId, from, to, page ?? 1, pageSize ?? 50, search),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/alerts", async (
            HttpContext context,
            IIctAlertMetricsQuery metrics,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasIctLogsAccess)
            {
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var tenantScope = access.IsSuperAdmin ? null : access.TenantId;
            var result = await metrics.GetAsync(tenantScope, ct);
            return Results.Ok(result);
        });

        return app;
    }
}
