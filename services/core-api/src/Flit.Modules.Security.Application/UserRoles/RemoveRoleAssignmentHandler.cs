using Flit.Modules.Security.Domain.UserRoles;

namespace Flit.Modules.Security.Application.UserRoles;

/// <summary>
/// Quita un rol puntual de un usuario en un tenant (HU #10506 AC3), sin afectar los demás
/// roles activos del usuario (modelo aditivo). Es un estado soportado que el usuario quede
/// con 0 roles activos tras la operación.
/// </summary>
public sealed class RemoveRoleAssignmentHandler(IUserRoleAssignmentRepository repo)
{
    public async Task HandleAsync(Guid userId, Guid tenantId, Guid roleId, Guid removedBy, CancellationToken ct)
    {
        var existing = await repo.GetActiveAssignmentAsync(userId, tenantId, roleId, ct);
        if (existing is null)
            throw new RoleAssignmentNotFoundException();

        await repo.SoftDeleteAssignmentAsync(existing.Id, removedBy, ct);
    }
}
