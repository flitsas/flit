using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13647 (Feature #13101 F-B, Épica #12753) — consulta de la auditoría de permisos individuales.
/// Super Admin: todo (tenantId opcional); los demás: solo su tenant del JWT (otro tenant = 403). Fechas en UTC ISO-8601.
/// </summary>
internal static class PermissionAuditEndpoints
{
    internal static void Map(RouteGroupBuilder group) =>
        group.MapGet("/permission-audit", async (
            ClaimsPrincipal caller, [FromQuery] Guid? userId, [FromQuery] Guid? tenantId, [FromQuery] Guid? actorId,
            [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int? page, [FromQuery] int? pageSize,
            QueryPermissionAuditHandler handler, CancellationToken ct) =>
        {
            if (!RequestTenantResolver.TryResolveTenantId(caller, out var callerTenant))
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await handler.HandleAsync(
                    callerTenant, RequestTenantResolver.IsSuperAdmin(caller), tenantId, userId, actorId, from, to, page, pageSize, ct)
                    .ConfigureAwait(false);
                return Results.Ok(new
                {
                    items = result.Items.Select(r => new
                    {
                        r.Id,
                        r.TenantId,
                        r.TenantName,
                        r.ActorUserId,
                        r.ActorName,
                        r.ActorEmail,
                        r.TargetUserId,
                        r.TargetName,
                        r.TargetEmail,
                        r.ProductCode,
                        r.ModuleCode,
                        r.PermissionSlug,
                        r.PermissionName,
                        r.ModuleName,
                        r.Action,
                        r.Outcome,
                        r.ReasonCode,
                        r.ValueBefore,
                        r.ValueAfter,
                        r.BatchId,
                        CreatedAt = r.CreatedAt.UtcDateTime,
                    }),
                    total = result.Total,
                    page = result.Page,
                    pageSize = result.PageSize,
                });
            }
            catch (PermissionAuditScopeException ex)
            {
                return Results.Json(new { code = "OUT_OF_SCOPE", message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
        })
        .RequireAuthorization(AdminAuthorization.PermissionOverridesAdminPolicy).WithName("ListPermissionAudit");
}
