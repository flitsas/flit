using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13644 (Feature #13101 F-B, Épica #12753, ADR-0071) — permisos individuales por usuario bajo
/// <c>/api/v1/security</c>. Solo administradores (<see cref="AdminAuthorization.PermissionOverridesAdminPolicy"/>).
/// El tenant destino es el del JWT; el Super Admin puede declararlo con <c>?tenantId=</c>. El tope, la bitácora y los
/// rechazos viven en <see cref="ManageUserPermissionOverridesHandler"/>; las reglas de ámbito (#13645) se enchufan allí
/// como <see cref="IPermissionOverrideRule"/>.
/// </summary>
internal static class PermissionOverridesEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        const string policy = AdminAuthorization.PermissionOverridesAdminPolicy;

        // AC5: catálogo otorgable (Super Admin: todo enforced = 'true'; el resto: su tope).
        group.MapGet("/permission-overrides/grantable", (
            ClaimsPrincipal caller, [FromQuery] Guid? tenantId, ManageUserPermissionOverridesHandler handler, CancellationToken ct) =>
            Run(caller, tenantId, async (tenant, actor) =>
                Results.Ok((await handler.ListGrantableAsync(tenant, actor, ct)).Select(p =>
                    new { p.Id, p.Slug, p.Name, p.ModuleCode, p.ProductCode }))))
            .RequireAuthorization(policy).WithName("ListGrantablePermissionOverrides");

        group.MapGet("/users/{userId:guid}/permission-overrides", (
            Guid userId, ClaimsPrincipal caller, [FromQuery] Guid? tenantId, ManageUserPermissionOverridesHandler handler,
            IPermissionAuditReader grants, CancellationToken ct) =>
            Run(caller, tenantId, async (tenant, actor) =>
            {
                var items = await handler.ListAsync(tenant, actor, userId, ct);
                // HU #13647 (FD1-8): quién otorgó y cuándo, una sola consulta; aditivo sobre el contrato de #13644.
                var info = (await grants.GetGrantInfoAsync(tenant, userId, ct)).ToDictionary(g => g.PermissionId);
                return Results.Ok(items.Select(i => new
                {
                    i.PermissionId,
                    i.Slug,
                    i.ProductCode,
                    i.Effect,
                    i.Source,
                    i.GrantedBy,
                    GrantedByName = info.TryGetValue(i.PermissionId, out var g) ? g.GrantedByName : null,
                    GrantedAt = info.TryGetValue(i.PermissionId, out var g2) ? (DateTime?)g2.GrantedAt.UtcDateTime : null,
                }));
            }))
            .RequireAuthorization(policy).WithName("ListUserPermissionOverrides");

        // AC1/AC2/AC3: otorgar (allow) o denegar (deny) un permiso puntual.
        group.MapPut("/users/{userId:guid}/permission-overrides/{permissionId:guid}", (
            Guid userId, Guid permissionId, [FromBody] SetOverrideRequest request, ClaimsPrincipal caller,
            [FromQuery] Guid? tenantId, ManageUserPermissionOverridesHandler handler, CancellationToken ct) =>
            Run(caller, tenantId, async (tenant, actor) =>
                Results.Ok(await handler.SetAsync(tenant, actor, userId, permissionId, request.Effect ?? string.Empty, ct))))
            .RequireAuthorization(policy).WithName("SetUserPermissionOverride");

        // AC4: quitar el permiso individual ("volver a lo que dicta el rol").
        group.MapDelete("/users/{userId:guid}/permission-overrides/{permissionId:guid}", (
            Guid userId, Guid permissionId, ClaimsPrincipal caller, [FromQuery] Guid? tenantId,
            ManageUserPermissionOverridesHandler handler, CancellationToken ct) =>
            Run(caller, tenantId, async (tenant, actor) =>
                await handler.RemoveAsync(tenant, actor, userId, permissionId, ct)
                    ? Results.NoContent()
                    : Error(StatusCodes.Status404NotFound, "OVERRIDE_NOT_FOUND", "El usuario no tiene un permiso individual para ese permiso.")))
            .RequireAuthorization(policy).WithName("RemoveUserPermissionOverride");

        // Módulo completo: foto de las acciones de hoy, limitada al tope.
        group.MapPost("/users/{userId:guid}/permission-overrides/modules/{moduleCode}", (
            Guid userId, string moduleCode, [FromBody] SetOverrideRequest request, ClaimsPrincipal caller,
            [FromQuery] Guid? tenantId, ManageUserPermissionOverridesHandler handler, CancellationToken ct) =>
            Run(caller, tenantId, async (tenant, actor) =>
                Results.Ok(await handler.GrantModuleAsync(tenant, actor, userId, moduleCode, request.Effect ?? string.Empty, ct))))
            .RequireAuthorization(policy).WithName("GrantModulePermissionOverrides");

        // HU #13646 (FD2-5): consulta previa al cambio de rol — cuántos permisos individuales perdería el usuario.
        group.MapGet("/users/{userId:guid}/permission-overrides/count", (
            Guid userId, ClaimsPrincipal caller, [FromQuery] Guid? tenantId, [FromQuery] string? productCode,
            ManageUserPermissionOverridesHandler handler, CancellationToken ct) =>
            Run(caller, tenantId, async (tenant, actor) =>
                Results.Ok(new { count = await handler.CountAsync(tenant, actor, userId, productCode, ct) })))
            .RequireAuthorization(policy).WithName("CountUserPermissionOverrides");
    }

    private static async Task<IResult> Run(ClaimsPrincipal caller, Guid? tenantParam, Func<Guid, OverrideActor, Task<IResult>> action)
    {
        var sub = caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var actorId) || !RequestTenantResolver.TryResolveTenantId(caller, out var callerTenant))
        {
            return Results.Unauthorized();
        }

        var isSuperAdmin = RequestTenantResolver.IsSuperAdmin(caller);
        var tenant = isSuperAdmin && tenantParam is { } t ? t : callerTenant;
        try
        {
            return await action(tenant, new OverrideActor(actorId, isSuperAdmin));
        }
        catch (PermissionOverrideRejectedException ex)
        {
            return RejectedResult(ex);
        }
        catch (PermissionNotFoundForOverrideException ex)
        {
            return Error(StatusCodes.Status404NotFound, "PERMISSION_NOT_FOUND", ex.Message);
        }
        catch (ModuleNotFoundForOverrideException ex)
        {
            return Error(StatusCodes.Status404NotFound, "MODULE_NOT_FOUND", ex.Message);
        }
        catch (ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_EFFECT", "El efecto debe ser 'allow' o 'deny'.");
        }
        catch (DuplicateUserPermissionOverrideException ex)
        {
            return Error(StatusCodes.Status409Conflict, "OVERRIDE_CONFLICT", ex.Message);
        }
    }

    /// <summary>HU #13645: USER_NOT_FOUND = 404, INVITATION_PENDING = 409, el resto de rechazos = 403 con su <c>code</c>.</summary>
    private static IResult RejectedResult(PermissionOverrideRejectedException ex) => ex.ReasonCode switch
    {
        PermissionOverrideReasons.UserNotFound => Error(StatusCodes.Status404NotFound, ex.ReasonCode, "El usuario no existe."),
        PermissionOverrideReasons.InvitationPending => Error(
            StatusCodes.Status409Conflict, ex.ReasonCode, "El usuario aún no aceptó su invitación.", ex.Slug),
        PermissionOverrideReasons.Self => Error(StatusCodes.Status403Forbidden, ex.ReasonCode, "No puedes modificar tus propios permisos.", ex.Slug),
        PermissionOverrideReasons.TargetSuperadmin => Error(
            StatusCodes.Status403Forbidden, ex.ReasonCode, "Al Super Admin no se le configuran permisos individuales.", ex.Slug),
        PermissionOverrideReasons.TargetIsAdmin => Error(
            StatusCodes.Status403Forbidden, ex.ReasonCode, "Solo el Super Admin modifica a otro administrador.", ex.Slug),
        PermissionOverrideReasons.OutOfScope => Error(
            StatusCodes.Status403Forbidden, ex.ReasonCode, "El usuario no pertenece a la compañía sobre la que operas.", ex.Slug),
        PermissionOverrideReasons.NotEnforced => Error(
            StatusCodes.Status403Forbidden, ex.ReasonCode, "Ese permiso aún no se exige en el sistema; configurarlo no tendría efecto.", ex.Slug),
        PermissionOverrideReasons.Excluded => Error(
            StatusCodes.Status403Forbidden, ex.ReasonCode, "Ese permiso no se configura individualmente.", ex.Slug),
        _ => Error(StatusCodes.Status403Forbidden, ex.ReasonCode, "El permiso queda fuera de lo que puedes otorgar.", ex.Slug),
    };

    private static IResult Error(int status, string code, string message, string? permission = null) =>
        Results.Json(new ErrorBody(code, message, permission), statusCode: status);

    private sealed record SetOverrideRequest(string? Effect);

    private sealed record ErrorBody(string Code, string Message, string? Permission);
}
