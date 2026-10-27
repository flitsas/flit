using Flit.Modules.Security.Domain.PermissionOverrides;
using Flit.Modules.Security.Domain.UserRoles;

namespace Flit.Modules.Security.Application.UserRoles;

/// <summary>
/// Quita un rol puntual de un usuario en un tenant (HU #10506 AC3), sin afectar los demás
/// roles activos del usuario (modelo aditivo). Es un estado soportado que el usuario quede
/// con 0 roles activos tras la operación.
/// <para>
/// HU #13646 (FB-15, hallazgo de review): al quitar el rol también se purgan los permisos individuales del usuario en ese tenant y
/// en el producto del rol quitado, en la misma transacción y con bitácora <c>ROLE_CHANGE_PURGE</c> (mismo batch), para que un
/// allow del producto no siga sumando al token. Si al usuario le queda otro rol ACTIVO del mismo producto en el tenant NO se purga:
/// el usuario sigue operando en ese producto y sus excepciones siguen siendo válidas. Un producto nulo/vacío se trata como
/// <c>tramites</c> (<see cref="RoleProductCode"/>). Los permisos de otros productos no se tocan.
/// </para>
/// </summary>
public sealed class RemoveRoleAssignmentHandler(
    IUserRoleAssignmentRepository repo,
    IRoleChangeOverridesPurger? purger = null)
{
    public async Task HandleAsync(
        Guid userId, Guid tenantId, Guid roleId, Guid removedBy, CancellationToken ct,
        bool callerIsSuperAdmin = false)
    {
        // Mismo alcance que AssignRoleHandler: el SuperAdmin opera sobre el tenant del usuario
        // destino, no sobre el suyo (el interno de FLIT).
        if (callerIsSuperAdmin)
            tenantId = await repo.GetUserTenantAsync(userId, ct) ?? tenantId;

        var existing = await repo.GetActiveAssignmentAsync(userId, tenantId, roleId, ct);
        if (existing is null)
            throw new RoleAssignmentNotFoundException();

        if (purger is null)
        {
            await repo.SoftDeleteAssignmentAsync(existing.Id, removedBy, ct);
            return;
        }

        // El producto del rol quitado se lee aunque el rol ya esté inactivo: sus overrides siguen vivos y hay que poder limpiarlos.
        var product = RoleProductCode.Normalize(await repo.GetRoleProductCodeAsync(roleId, ct));
        if (await OtherActiveRoleInProductAsync(userId, tenantId, roleId, product, ct))
        {
            await repo.SoftDeleteAssignmentAsync(existing.Id, removedBy, ct);
            return;
        }

        await purger.RunAtomicallyAsync(async () =>
        {
            await repo.SoftDeleteAssignmentAsync(existing.Id, removedBy, ct);
            await purger.PurgeAsync(tenantId, userId, product, removedBy, ct);
        }, ct);
    }

    private async Task<bool> OtherActiveRoleInProductAsync(Guid userId, Guid tenantId, Guid removedRoleId, string product, CancellationToken ct)
    {
        foreach (var assignment in await repo.GetActiveAssignmentsAsync(userId, tenantId, ct))
        {
            if (assignment.RoleId == removedRoleId)
                continue;

            // Solo un rol activo concede permisos: uno inactivo no cuenta como "rol restante" del producto.
            var role = await repo.GetActiveRoleAsync(assignment.RoleId, ct);
            if (role is not null && RoleProductCode.Normalize(role.ProductCode) == product)
                return true;
        }

        return false;
    }
}
