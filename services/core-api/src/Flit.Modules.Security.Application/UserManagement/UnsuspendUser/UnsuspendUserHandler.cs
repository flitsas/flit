using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;

namespace Flit.Modules.Security.Application.UserManagement.UnsuspendUser;

/// <summary>
/// HU #10619: levanta la suspensión/desactivación activa de un usuario (temporal vigente o
/// indefinida). Compartido entre <c>SecurityEndpoints</c> y <c>AdminOtEndpoints</c>.
/// </summary>
public sealed class UnsuspendUserHandler(IUserManagementRepository repo)
{
    public async Task HandleAsync(UnsuspendUserCommand cmd, CancellationToken ct)
    {
        var target = await repo.FindTargetAsync(cmd.UserId, includeDeleted: false, ct);
        if (target is null)
            throw new TargetUserNotFoundException();

        // AC3 — SuperAdmin actúa sobre el tenant REAL del usuario objetivo, no el propio.
        if (!cmd.CallerIsSuperAdmin && target.TenantId != cmd.CallerTenantId)
            throw new UserOutOfScopeException();

        var now = DateTimeOffset.UtcNow;
        var closed = await repo.CloseActiveSuspensionsAsync(target.TenantId, target.UserId, now, cmd.CallerId, ct);
        if (closed == 0)
            throw new NoActiveSuspensionException();
    }
}
