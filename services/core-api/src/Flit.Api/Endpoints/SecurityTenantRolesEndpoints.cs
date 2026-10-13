using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13441 (Feature #13437, Épica #12750) — roles propios de la compañía para su Admin de Compañía, bajo
/// <c>/api/v1/security/roles</c>. Reutiliza los handlers de <c>Flit.Modules.Security.Application/Roles</c> con alcance
/// por tenant (el del JWT). El listado <c>GET /roles</c> vive en <see cref="SecurityEndpoints"/> y sigue abierto a
/// cualquier usuario autenticado para poder asignar roles; aquí solo está la gestión.
/// Las rutas <c>/api/v1/superadmin/roles*</c> no cambian.
/// </summary>
internal static class SecurityTenantRolesEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        // Permisos que el caller puede ofrecer al armar un rol (selector de la pantalla).
        group.MapGet("/roles/grantable-permissions", async (
            ClaimsPrincipal caller,
            ListGrantablePermissionsHandler handler,
            CancellationToken ct) =>
        {
            if (!RequestTenantResolver.TryResolveTenantId(caller, out var tenantId))
                return Results.Unauthorized();

            var items = await handler.HandleAsync(tenantId, CallerPermissions(caller), ct);
            return Results.Ok(items);
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .WithName("ListGrantablePermissions");

        group.MapGet("/roles/{id:guid}", async (
            Guid id,
            ClaimsPrincipal caller,
            GetTenantRoleHandler handler,
            CancellationToken ct) =>
        {
            if (!RequestTenantResolver.TryResolveTenantId(caller, out var tenantId))
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await handler.HandleAsync(tenantId, id, ct));
            }
            catch (Exception ex) when (TenantRoleErrors.TryMap(ex) is { } mapped)
            {
                return mapped;
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .WithName("GetTenantRole");

        group.MapPost("/roles", async (
            CreateTenantRoleRequest request,
            ClaimsPrincipal caller,
            CreateTenantRoleHandler handler,
            CancellationToken ct) =>
        {
            if (!RequestTenantResolver.TryResolveTenantId(caller, out var tenantId))
                return Results.Unauthorized();

            try
            {
                var id = await handler.HandleAsync(
                    new CreateTenantRoleCommand(
                        tenantId,
                        request.TargetEntityType ?? "COMPANY",
                        request.Code ?? string.Empty,
                        request.Name ?? string.Empty,
                        request.Description,
                        request.ProductCode ?? "tramites",
                        request.PermissionIds ?? [],
                        CallerPermissions(caller)),
                    ct);
                return Results.Created($"/api/v1/security/roles/{id}", new { id });
            }
            catch (Exception ex) when (TenantRoleErrors.TryMap(ex) is { } mapped)
            {
                return mapped;
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .WithName("CreateTenantRole")
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.Create, "role", "ROLE"));

        group.MapPut("/roles/{id:guid}", async (
            Guid id,
            UpdateTenantRoleRequest request,
            ClaimsPrincipal caller,
            UpdateTenantRoleHandler handler,
            CancellationToken ct) =>
        {
            if (!RequestTenantResolver.TryResolveTenantId(caller, out var tenantId))
                return Results.Unauthorized();

            try
            {
                var detail = await handler.HandleAsync(
                    new UpdateTenantRoleCommand(tenantId, id, request.Name ?? string.Empty, request.Description), ct);
                return Results.Ok(detail);
            }
            catch (Exception ex) when (TenantRoleErrors.TryMap(ex) is { } mapped)
            {
                return mapped;
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .WithName("UpdateTenantRole")
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.Update, "role", "ROLE", "id"));

        group.MapPut("/roles/{id:guid}/permissions", async (
            Guid id,
            [FromBody] SetTenantPermissionsRequest request,
            ClaimsPrincipal caller,
            SetTenantRolePermissionsHandler handler,
            CancellationToken ct) =>
        {
            if (!RequestTenantResolver.TryResolveTenantId(caller, out var tenantId))
                return Results.Unauthorized();

            try
            {
                var detail = await handler.HandleAsync(
                    new SetTenantRolePermissionsCommand(tenantId, id, request.PermissionIds ?? [], CallerPermissions(caller)),
                    ct);
                return Results.Ok(detail);
            }
            catch (Exception ex) when (TenantRoleErrors.TryMap(ex) is { } mapped)
            {
                return mapped;
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .WithName("SetTenantRolePermissions")
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.Update, "role", "ROLE", "id"));

        group.MapDelete("/roles/{id:guid}", async (
            Guid id,
            ClaimsPrincipal caller,
            DeleteTenantRoleHandler handler,
            CancellationToken ct) =>
        {
            if (!RequestTenantResolver.TryResolveTenantId(caller, out var tenantId))
                return Results.Unauthorized();

            try
            {
                await handler.HandleAsync(tenantId, id, ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (TenantRoleErrors.TryMap(ex) is { } mapped)
            {
                return mapped;
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .WithName("DeleteTenantRole")
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.Delete, "role", "ROLE", "id"));
    }

    private static List<string> CallerPermissions(ClaimsPrincipal caller) =>
        [.. caller.FindAll("permissions").Select(c => c.Value)];

    private sealed record CreateTenantRoleRequest(
        string? TargetEntityType,
        string? Code,
        string? Name,
        string? Description,
        string? ProductCode,
        List<Guid>? PermissionIds);

    private sealed record UpdateTenantRoleRequest(string? Name, string? Description);

    private sealed record SetTenantPermissionsRequest(List<Guid>? PermissionIds);
}

/// <summary>
/// HU #13441 — traduce las excepciones de dominio de la gestión de roles del tenant a respuestas HTTP con código
/// explícito. Pública para poder probarla sin levantar la API.
/// </summary>
public static class TenantRoleErrors
{
    /// <summary>La respuesta HTTP de la excepción, o <c>null</c> si no es una de las conocidas.</summary>
    public static IResult? TryMap(Exception ex) => ex switch
    {
        // AC4
        InvalidTargetEntityTypeException => Json(StatusCodes.Status400BadRequest, "INVALID_TARGET_ENTITY_TYPE",
            "Una compañía solo puede crear roles de tipo COMPANY."),
        InvalidRoleInputException => Json(StatusCodes.Status400BadRequest, "INVALID_ROLE_INPUT",
            "El código (2 a 50 caracteres: letras, números, punto, guion o guion bajo) y el nombre (hasta 100) son obligatorios."),
        InvalidRoleProductException => Json(StatusCodes.Status400BadRequest, "INVALID_ROLE_PRODUCT",
            "El producto del rol no es válido."),
        RolePermissionProductMismatchException => Json(StatusCodes.Status400BadRequest, "ROLE_PERMISSION_PRODUCT_MISMATCH",
            "Un rol solo puede incluir permisos de módulos de su propio producto."),
        // AC3
        PrivilegeCeilingException p => Json(StatusCodes.Status403Forbidden, p.Code, CeilingMessage(p), p.Slugs),
        // AC5: otro tenant o inexistente = 404 sin revelar; global = 403 (es visible, pero de solo lectura).
        RoleNotFoundException => Results.NotFound(),
        RoleNotOwnedException => Json(StatusCodes.Status403Forbidden, "ROLE_READ_ONLY",
            "Los roles globales de FLIT son de solo lectura para una compañía."),
        // AC2
        RoleHasActiveUsersException => Json(StatusCodes.Status409Conflict, "ROLE_HAS_ACTIVE_USERS",
            "El rol está asignado a usuarios. Cámbialos de rol antes de eliminarlo."),
        RoleCodeDuplicateException => Json(StatusCodes.Status409Conflict, "ROLE_CODE_DUPLICATE",
            "Ya existe un rol con ese código en tu compañía o en el catálogo de FLIT."),
        _ => null,
    };

    private static string CeilingMessage(PrivilegeCeilingException p) => p.Code switch
    {
        PrivilegeCeilingCodes.PlatformOnly => "Esos permisos son de plataforma y solo los gestiona FLIT.",
        PrivilegeCeilingCodes.ModuleNotEnabled => "Esos permisos pertenecen a un módulo que no está habilitado para tu compañía.",
        PrivilegeCeilingCodes.NotHeld => "Solo puedes otorgar permisos que tú mismo tienes.",
        _ => "Alguno de los permisos no existe.",
    };

    private static IResult Json(int status, string code, string message, IReadOnlyList<string>? permissions = null) =>
        Results.Json(new { code, message, permissions }, statusCode: status);
}
