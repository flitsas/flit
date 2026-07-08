using Flit.Modules.Security.Application.Modules;
using Flit.Modules.Security.Domain.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.SuperAdmin;

internal static class SecurityModulesEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/modules", async (
            ListModulesHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(ct);
            return Results.Ok(items);
        }).WithName("ListModules");

        group.MapPost("/modules", async (
            CreateModuleRequest request,
            CreateModuleHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(
                    new CreateModuleCommand(request.Code, request.Name, request.Description, request.SortOrder),
                    ct);
                return Results.Created($"/api/v1/superadmin/modules/{id}", new { id });
            }
            catch (ModuleCodeDuplicateException)
            {
                return Results.Conflict(new { code = "MODULE_CODE_DUPLICATE" });
            }
        }).WithName("CreateModule");

        group.MapPut("/modules/{id:guid}", async (
            Guid id,
            UpdateModuleRequest request,
            UpdateModuleHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(
                    new UpdateModuleCommand(id, request.Name, request.Description, request.SortOrder),
                    ct);
                return Results.Ok();
            }
            catch (ModuleNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("UpdateModule");

        group.MapMethods("/modules/{id:guid}/deactivate", ["PATCH"], async (
            Guid id,
            DeactivateModuleHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(id, ct);
                return Results.Ok();
            }
            catch (ModuleNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("DeactivateModule");

        group.MapDelete("/modules/{id:guid}", async (
            Guid id,
            DeleteModuleHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(id, ct);
                return Results.NoContent();
            }
            catch (ModuleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ModuleHasActivePermissionsException)
            {
                return Results.Conflict(new { code = "MODULE_HAS_ACTIVE_PERMISSIONS" });
            }
        }).WithName("DeleteModule");

        group.MapMethods("/modules/{id:guid}/activate", ["PATCH"], async (
            Guid id,
            ISecurityModuleRepository repo,
            CancellationToken ct) =>
        {
            var module = await repo.GetByIdAsync(id, ct);
            if (module is null) return Results.NotFound();
            await repo.ActivateAsync(id, ct);
            return Results.Ok();
        }).WithName("ActivateModule");
    }

    private sealed record CreateModuleRequest(string Code, string Name, string? Description, short SortOrder);
    private sealed record UpdateModuleRequest(string Name, string? Description, short SortOrder);
}
