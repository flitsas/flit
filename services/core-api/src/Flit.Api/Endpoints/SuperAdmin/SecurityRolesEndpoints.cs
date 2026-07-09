using Flit.Admin.Application.Auditing;
using Flit.Api.Endpoints.Auditing;
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
        // AC2 — GET /roles?targetEntityType={COMPANY|TRANSIT_OFFICE} → catálogo global de roles
        // (HU #10505: ya no filtra por tenantId — security.roles no tiene esa columna).
        group.MapGet("/roles", async (
            string targetEntityType,
            ListRolesHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(targetEntityType, ct);
            return Results.Ok(items);
        }).WithName("ListRoles");

        // Detalle de un rol puntual (permisos + is_active exactos) — complementa el listado
        // resumido, que ya no basta para saber con certeza el estado tras un refresh de UI.
        group.MapGet("/roles/{id:guid}", async (
            Guid id,
            GetRoleHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var detail = await handler.HandleAsync(id, ct);
                return Results.Ok(detail);
            }
            catch (RoleNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("GetRole");

        // AC1 — POST /roles → 201 con id del nuevo rol
        group.MapPost("/roles", async (
            CreateRoleRequest request,
            CreateRoleHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(
                    new CreateRoleCommand(request.TargetEntityType, request.Code, request.Name, request.Description),
                    ct);
                return Results.Created($"/api/v1/superadmin/roles/{id}", new { id });
            }
            catch (RoleCodeDuplicateException)
            {
                return Results.Conflict(new { code = "ROLE_CODE_DUPLICATE" });
            }
            catch (InvalidTargetEntityTypeException)
            {
                return Results.BadRequest(new { code = "INVALID_TARGET_ENTITY_TYPE" });
            }
        }).WithName("CreateRole")
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.Create, "role", "ROLE"));

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
                    new SetRolePermissionsCommand(id, request.PermissionIds),
                    ct);
                return Results.Ok(detail);
            }
            catch (RoleNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("SetRolePermissions")
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.Update, "role", "ROLE", "id"));

        // HU #10508 AC1 — PATCH /roles/{id}/activate y /roles/{id}/deactivate → gobernanza
        // SuperAdmin sobre el ciclo de vida del rol del catálogo global (paralelo a
        // SecurityModulesEndpoints.ActivateModule/DeactivateModule).
        group.MapMethods("/roles/{id:guid}/activate", ["PATCH"], async (
            Guid id,
            SetRoleActiveHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(id, isActive: true, ct);
                return Results.Ok();
            }
            catch (RoleNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("ActivateRole")
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.Update, "role", "ROLE", "id"));

        group.MapMethods("/roles/{id:guid}/deactivate", ["PATCH"], async (
            Guid id,
            SetRoleActiveHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(id, isActive: false, ct);
                return Results.Ok();
            }
            catch (RoleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RoleSystemLockedException)
            {
                return Results.Conflict(new { code = "ROLE_SYSTEM_LOCKED" });
            }
        }).WithName("DeactivateRole")
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.Update, "role", "ROLE", "id"));

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
        }).WithName("DeleteRole")
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.Delete, "role", "ROLE", "id"));
    }

    private sealed record CreateRoleRequest(string TargetEntityType, string Code, string Name, string? Description);

    private sealed record SetPermissionsRequest(List<Guid> PermissionIds);
}
