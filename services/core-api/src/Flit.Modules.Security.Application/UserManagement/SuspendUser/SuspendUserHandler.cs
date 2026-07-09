using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;

namespace Flit.Modules.Security.Application.UserManagement.SuspendUser;

/// <summary>
/// HU #10619: unifica la suspensión temporal y la desactivación indefinida (mismo mecanismo,
/// <c>EndsAt</c> nulo o con valor) y corrige el alcance de tenant para SuperAdmin (AC3). Compartido
/// entre <c>SecurityEndpoints</c> (AdminCompany/SuperAdmin) y <c>AdminOtEndpoints</c>
/// (ot_admin/SuperAdmin) para eliminar la lógica duplicada que antes vivía inline en cada endpoint.
/// </summary>
public sealed class SuspendUserHandler(IUserManagementRepository repo)
{
    public async Task<Guid> HandleAsync(SuspendUserCommand cmd, CancellationToken ct)
    {
        var target = await repo.FindTargetAsync(cmd.UserId, includeDeleted: false, ct);
        if (target is null)
            throw new TargetUserNotFoundException();

        // AC3 — SuperAdmin actúa sobre el tenant REAL del usuario objetivo, no el propio.
        if (!cmd.CallerIsSuperAdmin && target.TenantId != cmd.CallerTenantId)
            throw new UserOutOfScopeException();

        // AC4 — un usuario nunca puede suspenderse/desactivarse a sí mismo.
        if (cmd.CallerId == cmd.UserId)
            throw new SelfSuspensionException();

        // AC4 — guarda de "último administrador activo": si el objetivo tiene algún rol
        // administrativo activo (SuperAdmin/AdminCompany/ot_admin) y es el ÚNICO admin
        // disponible en ese alcance (tenant, o global para SuperAdmin), se rechaza la acción.
        var adminRoles = await repo.GetActiveAdminRoleAssignmentsAsync(target.UserId, ct);
        foreach (var adminRole in adminRoles)
        {
            var scopeTenantId = adminRole.RoleCode == AdminRoleCodes.SuperAdmin
                ? (Guid?)null
                : adminRole.TenantId;

            var hasOtherActiveAdmins = await repo.HasOtherActiveAdminsAsync(
                adminRole.RoleCode, scopeTenantId, target.UserId, ct);

            if (!hasOtherActiveAdmins)
                throw new LastActiveAdminException();
        }

        var now = DateTimeOffset.UtcNow;
        await repo.CloseActiveSuspensionsAsync(target.TenantId, target.UserId, now, cmd.CallerId, ct);
        return await repo.CreateSuspensionAsync(
            target.TenantId, target.UserId, now, cmd.EndsAt?.ToUniversalTime(), cmd.Reason, cmd.CallerId, ct);
    }
}
