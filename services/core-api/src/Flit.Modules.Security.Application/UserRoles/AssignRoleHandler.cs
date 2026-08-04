using Flit.Modules.Security.Domain.UserRoles;

namespace Flit.Modules.Security.Application.UserRoles;

/// <summary>
/// Asigna EL rol de un usuario en un tenant: un usuario tiene un único rol activo, y lo que
/// define lo que puede hacer son los permisos de ese rol. Asignar reemplaza — se cierran las
/// asignaciones activas anteriores en el mismo tenant y se crea la nueva.
///
/// Esto revierte el modelo aditivo de la HU #10506 (varios roles activos, permisos = unión) por
/// decisión del responsable funcional: si un rol necesita hacer algo más, se le agrega el
/// permiso, no un segundo rol. El índice único de BD que garantiza la invariante volvió a ser
/// (user_id, tenant_id) — ver la migración RolUnicoPorUsuario.
/// </summary>
public sealed class AssignRoleHandler(IUserRoleAssignmentRepository repo)
{
    public async Task HandleAsync(AssignRoleCommand cmd, CancellationToken ct)
    {
        if (cmd.UserId == cmd.AssignedBy)
            throw new SelfRoleAssignmentException();

        // Alcance: el SuperAdmin administra usuarios de CUALQUIER tenant, así que el rol se
        // asigna en el tenant del usuario destino y no en el suyo (el interno de FLIT, que nunca
        // coincide). Para AdminCompany/ot_admin sigue mandando el tenant del caller, que es lo
        // que acota lo que pueden tocar.
        var tenantId = cmd.CallerIsSuperAdmin
            ? await repo.GetUserTenantAsync(cmd.UserId, ct) ?? throw new UserOutOfScopeException()
            : cmd.TenantId;

        // El usuario destino debe pertenecer a ese tenant.
        if (!await repo.UserBelongsToTenantAsync(cmd.UserId, tenantId, ct))
            throw new UserOutOfScopeException();

        // El rol debe existir y estar activo en el catálogo global (HU #10505).
        var role = await repo.GetActiveRoleAsync(cmd.RoleId, ct);
        if (role is null)
            throw new RoleForAssignmentNotFoundException();

        // HU #10506: el rol solo puede asignarse en un tenant del mismo TargetEntityType
        // (COMPANY | TRANSIT_OFFICE) para el que fue definido.
        var tenantTargetEntityType = await repo.GetTenantTargetEntityTypeAsync(tenantId, ct);
        if (!string.Equals(role.TargetEntityType, tenantTargetEntityType, StringComparison.Ordinal))
            throw new RoleTargetEntityTypeMismatchException();

        // Pedir el rol que el usuario ya tiene no es un cambio: se rechaza antes de cerrar nada,
        // para no dejarlo sin rol si algo fallara al recrearlo.
        var current = await repo.GetActiveAssignmentsAsync(cmd.UserId, tenantId, ct);
        if (current.Any(a => a.RoleId == cmd.RoleId))
            throw new RoleAlreadyAssignedException();

        // Reemplazo: se cierran TODAS las asignaciones activas anteriores del usuario en este
        // tenant. Normalmente es una sola; el bucle cubre los datos previos al rol único.
        foreach (var assignment in current)
            await repo.SoftDeleteAssignmentAsync(assignment.Id, cmd.AssignedBy, ct);

        await repo.CreateAssignmentAsync(
            new AssignRoleData(tenantId, cmd.UserId, cmd.RoleId, cmd.AssignedBy),
            ct);
    }
}
