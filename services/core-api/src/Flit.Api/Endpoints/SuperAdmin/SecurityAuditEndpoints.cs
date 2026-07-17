using Flit.Admin.Application.Auditing.GetAdminAuditLog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.SuperAdmin;

/// <summary>
/// HU #10679: consulta del rastro unificado de auditoría administrativa/seguridad
/// (config + usuarios/roles/permisos/autenticación, HU #10678), con alcance GLOBAL
/// (cross-tenant). Acceso EXCLUSIVO SuperAdmin — el grupo padre ya exige
/// <c>SuperAdminPolicy</c> (ver <see cref="SuperAdminEndpointExtensions"/>).
/// </summary>
internal static class SecurityAuditEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        // GET /api/v1/superadmin/audit — consulta paginada y filtrable del rastro de
        // auditoría (R1–R5 del requerimiento): por usuario (actor o afectado), por
        // compañía gestora u organismo de tránsito (tenantId + tenantType), por módulo,
        // operación, resultado y rango de fechas.
        group.MapGet("/audit", async (
            [FromServices] GetAdminAuditLogHandler handler,
            CancellationToken ct,
            [FromQuery] Guid? userId = null,
            [FromQuery] Guid? tenantId = null,
            [FromQuery] string? tenantType = null,
            [FromQuery] string? module = null,
            [FromQuery] string? operation = null,
            [FromQuery] string? result = null,
            [FromQuery] DateTimeOffset? dateFrom = null,
            [FromQuery] DateTimeOffset? dateTo = null,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null) =>
        {
            var query = new GetAdminAuditLogQuery(
                userId,
                tenantId,
                tenantType,
                module,
                operation,
                result,
                dateFrom,
                dateTo,
                page,
                pageSize);

            var response = await handler.HandleAsync(query, ct).ConfigureAwait(false);

            return Results.Ok(response);
        })
        .WithName("GetAdminAuditLog")
        .WithSummary("Consulta global del rastro de auditoría administrativa/seguridad (SuperAdmin)")
        .WithDescription("Retorna el rastro unificado de auditoría (config + operaciones "
            + "administrativas/seguridad sobre usuarios, roles, permisos y autenticación) "
            + "ordenado por changedAt descendente, paginado y filtrable por usuario "
            + "(actor o afectado), tenant, tenantType, módulo, operación, resultado y rango "
            + "de fechas. Alcance global (cross-tenant). Requiere rol SuperAdmin.")
        .Produces<GetAdminAuditLogResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);
    }
}
