using Flit.Modules.Security.Application.Permissions;
using Flit.Modules.Security.Domain.Permissions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.SuperAdmin;

internal static class SecurityPermissionsEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/permissions", async (
            Guid? moduleId,
            ListPermissionsHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(moduleId, ct);
            return Results.Ok(items);
        }).WithName("ListPermissions");

        group.MapPost("/permissions", async (
            CreatePermissionRequest request,
            CreatePermissionHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(
                    new CreatePermissionCommand(
                        request.ModuleId,
                        request.Slug,
                        request.Name,
                        request.Action,
                        request.HttpMethod,
                        request.RoutePattern,
                        request.Description),
                    ct);
                return Results.Created($"/api/v1/superadmin/permissions/{id}", new { id });
            }
            catch (ModuleInactiveException)
            {
                return Results.UnprocessableEntity(new { code = "MODULE_INACTIVE" });
            }
            catch (PermissionSlugDuplicateException)
            {
                return Results.Conflict(new { code = "PERMISSION_SLUG_DUPLICATE" });
            }
        }).WithName("CreatePermission");

        group.MapMethods("/permissions/{id:guid}/deactivate", ["PATCH"], async (
            Guid id,
            DeactivatePermissionHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(id, ct);
                return Results.Ok();
            }
            catch (PermissionNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("DeactivatePermission");

        group.MapDelete("/permissions/{id:guid}", async (
            Guid id,
            DeletePermissionHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(id, ct);
                return Results.NoContent();
            }
            catch (PermissionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (PermissionInUseException)
            {
                return Results.Conflict(new { code = "PERMISSION_IN_USE" });
            }
        }).WithName("DeletePermission");
    }

    private sealed record CreatePermissionRequest(
        Guid ModuleId,
        string Slug,
        string Name,
        string Action,
        string HttpMethod,
        string RoutePattern,
        string? Description);
}
