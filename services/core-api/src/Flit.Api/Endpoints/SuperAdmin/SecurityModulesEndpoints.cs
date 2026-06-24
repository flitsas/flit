using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Application.Modules;
using Flit.Modules.Security.Domain.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

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

        group.MapGet("/modules/{id:guid}/grants", async (
            Guid id,
            FlitDbContext db,
            CancellationToken ct) =>
        {
            var grants = await (
                from g in db.TenantModuleGrants.AsNoTracking()
                join t in db.Tenants.AsNoTracking() on g.TenantId equals t.Id
                where g.ModuleId == id
                select new TenantModuleGrantDto(g.TenantId.ToString(), t.LegalName)
            ).ToListAsync(ct);
            return Results.Ok(grants);
        }).WithName("ListModuleGrants");

        group.MapPost("/modules/{id:guid}/grants/{tenantId:guid}", async (
            Guid id,
            Guid tenantId,
            FlitDbContext db,
            CancellationToken ct) =>
        {
            var exists = await db.TenantModuleGrants
                .AnyAsync(g => g.ModuleId == id && g.TenantId == tenantId, ct);
            if (exists) return Results.Ok();

            db.TenantModuleGrants.Add(new TenantModuleGrant
            {
                TenantId = tenantId,
                ModuleId = id,
                GrantedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return Results.Created();
        }).WithName("GrantModuleToTenant");

        group.MapDelete("/modules/{id:guid}/grants/{tenantId:guid}", async (
            Guid id,
            Guid tenantId,
            FlitDbContext db,
            CancellationToken ct) =>
        {
            var grant = await db.TenantModuleGrants
                .FirstOrDefaultAsync(g => g.ModuleId == id && g.TenantId == tenantId, ct);
            if (grant is null) return Results.NoContent();

            db.TenantModuleGrants.Remove(grant);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithName("RevokeModuleFromTenant");
    }

    private sealed record CreateModuleRequest(string Code, string Name, string? Description, short SortOrder);
    private sealed record UpdateModuleRequest(string Name, string? Description, short SortOrder);
    private sealed record TenantModuleGrantDto(string TenantId, string TenantName);
}
