using Flit.Modules.Security.Domain.UserRoles;

namespace Flit.Modules.Security.Application.UserRoles;

/// <summary>
/// Asigna un rol a un usuario en un tenant. HU #10506: modelo ADITIVO — un usuario puede tener
/// varios roles activos simultáneos (permisos = unión); asignar un segundo rol NO reemplaza ni
/// toca las asignaciones activas existentes. Solo se rechaza la asignación EXACTA duplicada
/// (mismo usuario + tenant + rol ya activo).
/// </summary>
public sealed class AssignRoleHandler(IUserRoleAssignmentRepository repo)
{
    public async Task HandleAsync(AssignRoleCommand cmd, CancellationToken ct)
    {
        if (cmd.UserId == cmd.AssignedBy)
            throw new SelfRoleAssignmentException();

        // El usuario destino debe pertenecer al tenant del caller (vía HomeTenantId).
        if (!await repo.UserBelongsToTenantAsync(cmd.UserId, cmd.TenantId, ct))
            throw new UserOutOfScopeException();

        // El rol debe existir y estar activo en el catálogo global (HU #10505).
        var role = await repo.GetActiveRoleAsync(cmd.RoleId, ct);
        if (role is null)
            throw new RoleForAssignmentNotFoundException();

        // HU #10506: el rol solo puede asignarse en un tenant del mismo TargetEntityType
        // (COMPANY | TRANSIT_OFFICE) para el que fue definido.
        var tenantTargetEntityType = await repo.GetTenantTargetEntityTypeAsync(cmd.TenantId, ct);
        if (!string.Equals(role.TargetEntityType, tenantTargetEntityType, StringComparison.Ordinal))
            throw new RoleTargetEntityTypeMismatchException();

        // AC2 — rechazo de asignación duplicada del mismo rol ya activo, sin duplicar fila.
        var existing = await repo.GetActiveAssignmentAsync(cmd.UserId, cmd.TenantId, cmd.RoleId, ct);
        if (existing is not null)
            throw new RoleAlreadyAssignedException();

        // AC1 — modelo aditivo: crea la nueva asignación SIN tocar las demás asignaciones
        // activas del usuario (a diferencia del reemplazo de HU #10164).
        await repo.CreateAssignmentAsync(
            new AssignRoleData(cmd.TenantId, cmd.UserId, cmd.RoleId, cmd.AssignedBy),
            ct);
    }
}
