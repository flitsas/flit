using Flit.Modules.Security.Domain.PermissionOverrides;

namespace Flit.Modules.Security.Application.PermissionOverrides;

/// <summary>Quién opera: su id y si es Super Admin FLIT (sin tope, solo limitado por <c>enforced = 'true'</c>).</summary>
public sealed record OverrideActor(Guid UserId, bool IsSuperAdmin);

/// <summary>
/// Tope de un administrador (ADR-0071, FB-5): unión de los permisos de sus roles en el tenant MENOS los que tenga
/// denegados individualmente. Sus <c>allow</c> individuales NO lo amplían. Se calcula en el momento de otorgar.
/// </summary>
public sealed class PermissionCap
{
    private readonly IReadOnlySet<Guid> _ids;

    private PermissionCap(bool unlimited, IReadOnlySet<Guid> ids)
    {
        IsUnlimited = unlimited;
        _ids = ids;
    }

    public bool IsUnlimited { get; }

    public static PermissionCap Unlimited { get; } = new(true, new HashSet<Guid>());

    public bool Contains(Guid permissionId) => IsUnlimited || _ids.Contains(permissionId);

    /// <summary>Cálculo puro: permisos de los roles menos los <c>deny</c> del propio administrador; ignora sus <c>allow</c>.</summary>
    public static PermissionCap FromRoles(IEnumerable<Guid> rolePermissionIds, IEnumerable<UserPermissionOverride> actorOverrides)
    {
        ArgumentNullException.ThrowIfNull(rolePermissionIds);
        ArgumentNullException.ThrowIfNull(actorOverrides);
        var ids = rolePermissionIds.ToHashSet();
        ids.ExceptWith(actorOverrides.Where(o => o.Effect == PermissionEffects.Deny).Select(o => o.PermissionId));
        return new PermissionCap(false, ids);
    }
}

/// <summary>Servicio reutilizable del tope (lo usan #13645 y la emisión de F-D2): lee roles y denies de la BD en el momento.</summary>
public sealed class PermissionCapCalculator(IUserPermissionOverrideRepository repository)
{
    public async Task<PermissionCap> GetCapAsync(Guid tenantId, OverrideActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.IsSuperAdmin)
        {
            return PermissionCap.Unlimited;
        }

        var rolePermissionIds = await repository.GetRolePermissionIdsAsync(tenantId, actor.UserId, ct).ConfigureAwait(false);
        var overrides = await repository.ListActiveAsync(tenantId, actor.UserId, ct).ConfigureAwait(false);
        return PermissionCap.FromRoles(rolePermissionIds, overrides);
    }
}

/// <summary>Operación en curso, para las reglas de extensión.</summary>
public sealed record PermissionOverrideRequest(Guid TenantId, OverrideActor Actor, Guid TargetUserId, string Action, string? PermissionSlug);

/// <summary>
/// Punto de extensión (#13645): reglas de ámbito, otro administrador, sí mismo, invitación pendiente, excluidos, etc.
/// Se ejecutan ANTES de cualquier operación; devuelven el <c>reason_code</c> del rechazo o <c>null</c> si la dejan pasar.
/// Hoy no hay ninguna registrada.
/// </summary>
public interface IPermissionOverrideRule
{
    Task<string?> CheckAsync(PermissionOverrideRequest request, CancellationToken ct);
}
