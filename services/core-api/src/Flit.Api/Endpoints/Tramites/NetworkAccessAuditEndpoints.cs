using Flit.Api.Authorization;
using Flit.Tramites.Application.Auditing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #12361 (Feature #12257, épica #12235) — consulta de la auditoría del acceso consolidado
/// (<c>tramites.network_access_audit</c>). Dos lectores, ninguno con datos personales (solo ids,
/// recurso, instante y resultado):
/// <list type="bullet">
///   <item><c>GET /api/v1/admin/platform/network-access-audit</c> (SuperAdmin, AC4): quién de la
///   Concesión accedió a los datos de un cliente, con filtros por cliente, rango, recurso y paginación.</item>
///   <item><c>GET /api/v1/tramites/network-access-audit/mine</c> (AC2/AC7): el cliente HIJO consulta
///   quién accedió a SUS datos. El tenant sale de <c>tramites.tenantId</c> (middleware), jamás del
///   caller; la ruta está declarada en <c>RuntimeScopedRoutes</c>.</item>
/// </list>
/// Paginación acotada en servidor (<see cref="NetworkAccessAuditQuery.MaxTake"/>).
/// </summary>
internal static class NetworkAccessAuditEndpoints
{
    internal const string AdminRoute = "/api/v1/admin/platform/network-access-audit";
    internal const string MineRoutePrefix = "/api/v1/tramites/network-access-audit";

    internal static IEndpointRouteBuilder MapNetworkAccessAuditEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(AdminRoute, async (
            INetworkAccessAuditReader reader,
            [FromQuery] Guid? tenantId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] string? resource,
            [FromQuery] int? page,
            [FromQuery] int? take,
            CancellationToken ct) =>
        {
            var query = new NetworkAccessAuditQuery(tenantId, from, to, resource, page ?? 1, take ?? NetworkAccessAuditQuery.DefaultTake);
            var (items, total) = await reader.SearchAsync(query, ct);
            return Results.Ok(new NetworkAccessAuditPage(items, total, query.EffectivePage, query.EffectiveTake));
        })
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Plataforma · Jerarquía de clientes")
            .WithName("AdminNetworkAccessAuditSearch")
            .WithSummary("Auditoría de accesos consolidados de una cabeza de red a los datos de un cliente (SuperAdmin)")
            .Produces<NetworkAccessAuditPage>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapGet(MineRoutePrefix + "/mine", async (
            HttpContext http,
            INetworkAccessAuditReader reader,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] string? resource,
            [FromQuery] int? page,
            [FromQuery] int? take,
            CancellationToken ct) =>
        {
            // El tenant lo dejó el TenantEnforcementMiddleware (ruta RuntimeScoped): un SuperAdmin sin
            // X-Tenant-Id no tiene «mis datos» — usa la ruta de plataforma.
            var (tenantId, _) = RequestTenantResolver.FromItems(http);
            if (tenantId is not { } own || own == Guid.Empty)
                return Results.Json(new { error = "tenant_required" }, statusCode: StatusCodes.Status403Forbidden);

            var query = new NetworkAccessAuditQuery(own, from, to, resource, page ?? 1, take ?? NetworkAccessAuditQuery.DefaultTake);
            var (items, total) = await reader.SearchAsync(query, ct);
            return Results.Ok(new NetworkAccessAuditPage(items, total, query.EffectivePage, query.EffectiveTake));
        })
            .WithTags("Trámites · Red (vista consolidada)")
            .WithName("NetworkAccessAuditMine")
            .WithSummary("Quién de la cabeza de red accedió a los datos de mi cliente (cliente hijo)")
            .Produces<NetworkAccessAuditPage>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }
}

/// <summary>Página de la auditoría de accesos consolidados (sin datos personales).</summary>
public sealed record NetworkAccessAuditPage(
    IReadOnlyList<NetworkAccessAuditRow> Items,
    int Total,
    int Page,
    int Take);
