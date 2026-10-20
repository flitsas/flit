using Flit.Modules.Security.Domain.PermissionOverrides;

namespace Flit.Modules.Security.Application.PermissionOverrides;

/// <summary>Acción de lectura (GET de los overrides de un usuario): solo le aplican las reglas de existencia y ámbito.</summary>
public static class PermissionOverrideActions
{
    public const string Read = "READ";

    public static bool IsRead(string action) => action == Read;
}

/// <summary>Consulta memoizada (por solicitud) del estado de un usuario en un tenant; la comparten todas las reglas.</summary>
public sealed class OverrideTargetLookup(IOverrideTargetDirectory directory)
{
    private readonly Dictionary<(Guid Tenant, Guid User), OverrideTargetInfo> _cache = [];

    public async Task<OverrideTargetInfo> GetAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!_cache.TryGetValue((tenantId, userId), out var info))
        {
            info = await directory.GetAsync(tenantId, userId, ct).ConfigureAwait(false);
            _cache[(tenantId, userId)] = info;
        }

        return info;
    }
}

/// <summary>"Administrador" (HU #13645): roles SuperAdmin, AdminCompany, ot_admin y admin_tramites.</summary>
public static class AdministratorRoles
{
    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuperAdmin", "AdminCompany", "ot_admin", "admin_tramites",
    };

    public static bool Any(IEnumerable<string> roleCodes) => roleCodes.Any(Codes.Contains);
}

/// <summary>El usuario destino debe existir. Si el tenant no existe se lanza sin auditar (la bitácora exige tenant válido).</summary>
public sealed class UserExistsRule(OverrideTargetLookup lookup) : IPermissionOverrideRule
{
    public async Task<string?> CheckAsync(PermissionOverrideRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = await lookup.GetAsync(request.TenantId, request.TargetUserId, ct).ConfigureAwait(false);
        if (!target.TenantExists)
        {
            throw new PermissionOverrideRejectedException(PermissionOverrideReasons.UserNotFound, request.TargetUserId.ToString());
        }

        return target.UserExists ? null : PermissionOverrideReasons.UserNotFound;
    }
}

/// <summary>AC2: ningún administrador modifica sus propios permisos individuales (<c>SELF</c>).</summary>
public sealed class SelfEditRule : IPermissionOverrideRule
{
    public Task<string?> CheckAsync(PermissionOverrideRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(
            !PermissionOverrideActions.IsRead(request.Action) && request.Actor.UserId == request.TargetUserId
                ? PermissionOverrideReasons.Self
                : null);
    }
}

/// <summary>AC3: nadie (ni el Super Admin) configura permisos individuales a un usuario con rol SuperAdmin.</summary>
public sealed class TargetSuperAdminRule(OverrideTargetLookup lookup) : IPermissionOverrideRule
{
    public async Task<string?> CheckAsync(PermissionOverrideRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (PermissionOverrideActions.IsRead(request.Action))
        {
            return null;
        }

        var target = await lookup.GetAsync(request.TenantId, request.TargetUserId, ct).ConfigureAwait(false);
        return target.IsSuperAdmin ? PermissionOverrideReasons.TargetSuperadmin : null;
    }
}

/// <summary>
/// AC5 y AC7: el usuario destino debe pertenecer al tenant en el que se opera. Un Admin de Compañía no alcanza a
/// usuarios de hijas ni de otras compañías; un Admin OT, solo a los de su OT; el Super Admin actúa sobre el tenant que
/// declara. El actor no Super Admin también debe pertenecer a ese tenant (el JWT no basta).
/// </summary>
public sealed class TenantScopeRule(OverrideTargetLookup lookup) : IPermissionOverrideRule
{
    public async Task<string?> CheckAsync(PermissionOverrideRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = await lookup.GetAsync(request.TenantId, request.TargetUserId, ct).ConfigureAwait(false);
        if (!target.IsMember || (target.HomeTenantId is { } home && home != request.TenantId))
        {
            return PermissionOverrideReasons.OutOfScope;
        }

        if (!request.Actor.IsSuperAdmin)
        {
            var actor = await lookup.GetAsync(request.TenantId, request.Actor.UserId, ct).ConfigureAwait(false);
            if (!actor.IsMember)
            {
                return PermissionOverrideReasons.OutOfScope;
            }
        }

        return null;
    }
}

/// <summary>AC1: un administrador que no es Super Admin no modifica a otro administrador (AdminCompany, ot_admin, admin_tramites).</summary>
public sealed class TargetIsAdminRule(OverrideTargetLookup lookup) : IPermissionOverrideRule
{
    public async Task<string?> CheckAsync(PermissionOverrideRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Actor.IsSuperAdmin || PermissionOverrideActions.IsRead(request.Action))
        {
            return null;
        }

        var target = await lookup.GetAsync(request.TenantId, request.TargetUserId, ct).ConfigureAwait(false);
        return AdministratorRoles.Any(target.RoleCodesInTenant) ? PermissionOverrideReasons.TargetIsAdmin : null;
    }
}

/// <summary>AC4: permisos <c>enforced = 'excluded'</c> (usuarios.manage, rbac.manage, auth.me.read, banners.manage) nunca se configuran.</summary>
public sealed class ExcludedPermissionRule : IPermissionOverrideRule
{
    public Task<string?> CheckAsync(PermissionOverrideRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(
            request.PermissionEnforced == "excluded" ? PermissionOverrideReasons.Excluded : null);
    }
}

/// <summary>
/// Los permisos OT (<c>ot.*</c>) solo se configuran en usuarios de un organismo de tránsito: en un usuario de compañía no
/// tendrían efecto. Quitar uno que ya exista (REVOKE) sí se permite, para limpiar.
/// </summary>
public sealed class OtPermissionTenantRule(IOverrideTargetDirectory directory) : IPermissionOverrideRule
{
    public async Task<string?> CheckAsync(PermissionOverrideRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (PermissionOverrideActions.IsRead(request.Action) || request.Action == "REVOKE" || !OtPermissions.IsOt(request.PermissionSlug))
        {
            return null;
        }

        return await directory.IsTransitOfficeTenantAsync(request.TenantId, ct).ConfigureAwait(false)
            ? null
            : PermissionOverrideReasons.OtNotApplicable;
    }
}

/// <summary>AC6: un usuario que aún no aceptó su invitación no se configura; el flujo de invitación no cambia.</summary>
public sealed class InvitationPendingRule(OverrideTargetLookup lookup) : IPermissionOverrideRule
{
    public async Task<string?> CheckAsync(PermissionOverrideRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (PermissionOverrideActions.IsRead(request.Action))
        {
            return null;
        }

        var target = await lookup.GetAsync(request.TenantId, request.TargetUserId, ct).ConfigureAwait(false);
        return target.HasPendingInvitation || string.Equals(target.Status, "pending", StringComparison.OrdinalIgnoreCase)
            ? PermissionOverrideReasons.InvitationPending
            : null;
    }
}
