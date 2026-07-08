using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;

namespace Flit.Modules.Security.Application.UserManagement.DeleteUser;

/// <summary>
/// HU #10623: elimina (soft-delete reversible) a un usuario dentro del alcance del caller
/// (AdminCompany/ot_admin solo su tenant, SuperAdmin cualquiera).
///
/// AC1: solo marca <c>User.DeletedAt</c>/<c>DeletedBy</c> — el login ya rechaza cuentas con
///      <c>DeletedAt != null</c> (<c>AuthUserRepository.FindByEmailAsync</c>) y los listados de
///      usuarios de <c>SecurityEndpoints</c>/<c>AdminOtEndpoints</c> ya filtran
///      <c>DeletedAt == null</c>, así que el usuario desaparece de inmediato de ambos.
/// AC2: rechaza la auto-eliminación y la eliminación del último administrador activo del
///      tenant/sistema — misma guarda de "último admin" que <c>SuspendUserHandler</c> (HU #10619
///      AC4), reutilizada tal cual sin reimplementarla.
/// AC3: NO toca <c>UserRoleAssignment</c> ni <c>UserTempSuspension</c> — al restaurar,
///      <c>RestoreUserHandler</c> recupera exactamente el mismo estado porque nunca se modificó.
/// </summary>
public sealed class DeleteUserHandler(IUserManagementRepository repo)
{
    public async Task HandleAsync(DeleteUserCommand cmd, CancellationToken ct)
    {
        var target = await repo.FindTargetAsync(cmd.UserId, includeDeleted: false, ct);
        if (target is null)
            throw new TargetUserNotFoundException();

        // AC — SuperAdmin actúa sobre el tenant REAL del usuario objetivo, no el propio (mismo
        // criterio que SuspendUserCommand/UpdateUserCommand).
        if (!cmd.CallerIsSuperAdmin && target.TenantId != cmd.CallerTenantId)
            throw new UserOutOfScopeException();

        // AC2 — un usuario nunca puede eliminarse a sí mismo.
        if (cmd.CallerId == cmd.UserId)
            throw new SelfDeletionException();

        // AC2 — guarda de "último administrador activo": si el objetivo tiene algún rol
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

        await repo.SoftDeleteUserAsync(target.UserId, DateTimeOffset.UtcNow, cmd.CallerId, cmd.RowVersion, ct);
    }
}
