using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.SuperAdmin;

internal static class SecurityRolesEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        // AC2 — GET /roles?tenantId={guid} → lista de roles con permissionCount
        group.MapGet("/roles", async (
            Guid tenantId,
            ListRolesHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(tenantId, ct);
            return Results.Ok(items);
        }).WithName("ListRoles");

        // AC1 — POST /roles → 201 con id del nuevo rol
        group.MapPost("/roles", async (
            CreateRoleRequest request,
            CreateRoleHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(
                    new CreateRoleCommand(request.TenantId, request.Code, request.Name, request.Description),
                    ct);
                return Results.Created($"/api/v1/superadmin/roles/{id}", new { id });
            }
            catch (RoleCodeDuplicateException)
            {
                return Results.Conflict(new { code = "ROLE_CODE_DUPLICATE" });
            }
        }).WithName("CreateRole");

        // AC7 — PUT /roles/{id}/permissions → reemplaza todos los permisos del rol, retorna 200
        group.MapPut("/roles/{id:guid}/permissions", async (
            Guid id,
            SetPermissionsRequest request,
            SetRolePermissionsHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var detail = await handler.HandleAsync(
                    new SetRolePermissionsCommand(id, request.TenantId, request.PermissionIds),
                    ct);
                return Results.Ok(detail);
            }
            catch (RoleNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("SetRolePermissions");

        // AC3, AC4, AC5 — DELETE /roles/{id}
        group.MapDelete("/roles/{id:guid}", async (
            Guid id,
            DeleteRoleHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(id, ct);
                return Results.NoContent();
            }
            catch (RoleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RoleSystemLockedException)
            {
                return Results.Conflict(new { code = "ROLE_SYSTEM_LOCKED" });
            }
            catch (RoleHasActiveUsersException)
            {
                return Results.Conflict(new { code = "ROLE_HAS_ACTIVE_USERS" });
            }
        }).WithName("DeleteRole");
    }

    private sealed record CreateRoleRequest(Guid TenantId, string Code, string Name, string? Description);

    private sealed record SetPermissionsRequest(Guid TenantId, List<Guid> PermissionIds);
}
