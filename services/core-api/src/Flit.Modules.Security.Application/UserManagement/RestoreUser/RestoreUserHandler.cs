using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;

namespace Flit.Modules.Security.Application.UserManagement.RestoreUser;

/// <summary>
/// HU #10623: restaura a un usuario soft-deleted — SOLO SuperAdmin.
///
/// AC3: como <c>DeleteUserHandler</c> nunca toca <c>UserRoleAssignment</c> ni
///      <c>UserTempSuspension</c>, restaurar recupera exactamente los mismos roles y el mismo
///      estado de suspensión que el usuario tenía al momento de eliminarse.
/// AC5: restaurar un usuario que NO está eliminado se rechaza explícitamente con
///      <see cref="UserNotDeletedException"/> — no es un no-op silencioso.
/// </summary>
public sealed class RestoreUserHandler(IUserManagementRepository repo)
{
    public async Task HandleAsync(RestoreUserCommand cmd, CancellationToken ct)
    {
        // includeDeleted: true — el objetivo de una restauración ES un usuario eliminado; con el
        // filtro por defecto (false) FindTargetAsync siempre devolvería null.
        var target = await repo.FindTargetAsync(cmd.UserId, includeDeleted: true, ct);
        if (target is null)
            throw new TargetUserNotFoundException();

        if (target.DeletedAt is null)
            throw new UserNotDeletedException();

        await repo.RestoreUserAsync(target.UserId, cmd.CallerId, ct);
    }
}
