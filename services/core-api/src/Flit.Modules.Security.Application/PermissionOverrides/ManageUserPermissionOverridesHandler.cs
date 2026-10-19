using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;

namespace Flit.Modules.Security.Application.PermissionOverrides;

/// <summary><c>Changed</c> = false cuando el usuario ya tenía exactamente ese efecto (idempotente, sin bitácora).</summary>
public sealed record SetOverrideResult(Guid PermissionId, string Effect, bool Changed);

public sealed record UserOverrideItem(Guid PermissionId, string Slug, string ProductCode, string Effect, string Source, Guid GrantedBy);

/// <summary>
/// HU #13644 (ADR-0071, FB-5 a FB-8): otorgar, denegar y quitar permisos individuales dentro del tope del administrador.
/// Habilitar (allow) o "volver al rol" cuando el resultado queda fuera del tope se rechaza; quitar o denegar siempre se puede.
/// Cada operación escribe la bitácora; cada rechazo queda como <c>REJECTED</c> con su <c>reason_code</c> y lanza
/// <see cref="PermissionOverrideRejectedException"/>. No hay cascada: perder el tope después no toca lo ya otorgado.
/// </summary>
public sealed class ManageUserPermissionOverridesHandler(
    IUserPermissionOverrideRepository repository,
    PermissionCapCalculator capCalculator,
    GrantModuleOverridesHandler moduleHandler,
    IEnumerable<IPermissionOverrideRule> rules)
{
    public async Task<SetOverrideResult> SetAsync(
        Guid tenantId, OverrideActor actor, Guid targetUserId, Guid permissionId, string effect, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!PermissionEffects.IsValid(effect))
        {
            throw new ArgumentException("El efecto debe ser 'allow' o 'deny'.", nameof(effect));
        }

        var permission = await GetPermissionAsync(permissionId, ct).ConfigureAwait(false);
        var action = effect == PermissionEffects.Allow ? "GRANT" : "DENY";
        var existing = await FindActiveAsync(tenantId, targetUserId, permissionId, ct).ConfigureAwait(false);

        await RunRulesAsync(tenantId, actor, targetUserId, action, permission, existing?.Effect, effect, ct).ConfigureAwait(false);
        if (effect == PermissionEffects.Allow)
        {
            if (!permission.IsEnforced)
            {
                await RejectAsync(tenantId, actor, targetUserId, permission, action, PermissionOverrideReasons.NotEnforced, existing?.Effect, effect, ct)
                    .ConfigureAwait(false);
            }

            var cap = await capCalculator.GetCapAsync(tenantId, actor, ct).ConfigureAwait(false);
            if (!cap.Contains(permissionId))
            {
                await RejectAsync(tenantId, actor, targetUserId, permission, action, PermissionOverrideReasons.OutOfCap, existing?.Effect, effect, ct)
                    .ConfigureAwait(false);
            }
        }

        if (existing?.Effect == effect)
        {
            return new SetOverrideResult(permissionId, effect, false);
        }

        var added = new UserPermissionOverride(
            Guid.NewGuid(), tenantId, targetUserId, permission.ProductCode, permissionId, effect,
            PermissionOverrideSources.Individual, null, actor.UserId);
        await repository.ApplyAsync(
            tenantId, actor.UserId, existing is null ? [] : [existing.Id], [added],
            [Audit(tenantId, actor, targetUserId, permission, action, "OK", null, existing?.Effect, effect)], ct).ConfigureAwait(false);
        return new SetOverrideResult(permissionId, effect, true);
    }

    /// <summary>Quita el override ("volver a lo que dicta el rol"). <c>false</c> si no había ninguno activo.</summary>
    public async Task<bool> RemoveAsync(
        Guid tenantId, OverrideActor actor, Guid targetUserId, Guid permissionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var permission = await GetPermissionAsync(permissionId, ct).ConfigureAwait(false);
        var existing = await FindActiveAsync(tenantId, targetUserId, permissionId, ct).ConfigureAwait(false);
        await RunRulesAsync(tenantId, actor, targetUserId, "REVOKE", permission, existing?.Effect, null, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        // Quitar un allow solo reduce. Quitar un deny devuelve lo que dicta el rol: si el rol lo da y queda fuera del tope, no.
        if (existing.Effect == PermissionEffects.Deny)
        {
            var targetRolePermissions = await repository.GetRolePermissionIdsAsync(tenantId, targetUserId, ct).ConfigureAwait(false);
            if (targetRolePermissions.Contains(permissionId)
                && !(await capCalculator.GetCapAsync(tenantId, actor, ct).ConfigureAwait(false)).Contains(permissionId))
            {
                await RejectAsync(tenantId, actor, targetUserId, permission, "REVOKE", PermissionOverrideReasons.OutOfCap, existing.Effect, null, ct)
                    .ConfigureAwait(false);
            }
        }

        await repository.ApplyAsync(
            tenantId, actor.UserId, [existing.Id], [],
            [Audit(tenantId, actor, targetUserId, permission, "REVOKE", "OK", null, existing.Effect, null)], ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Módulo completo (foto) limitado al tope: las acciones fuera del tope o sin <c>enforced</c> no se otorgan.</summary>
    public async Task<GrantModuleOverridesResult> GrantModuleAsync(
        Guid tenantId, OverrideActor actor, Guid targetUserId, string moduleCode, string effect, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await RunRulesAsync(tenantId, actor, targetUserId, "MODULE_SNAPSHOT", null, null, effect, ct, moduleCode).ConfigureAwait(false);

        IReadOnlySet<Guid>? allowed = null;
        if (effect == PermissionEffects.Allow)
        {
            var cap = await capCalculator.GetCapAsync(tenantId, actor, ct).ConfigureAwait(false);
            allowed = (await repository.ListEnforcedPermissionsAsync(ct).ConfigureAwait(false))
                .Where(p => cap.Contains(p.Id)).Select(p => p.Id).ToHashSet();
        }

        var result = await moduleHandler.HandleAsync(
            new GrantModuleOverridesCommand(
                tenantId, targetUserId, actor.UserId, moduleCode, effect, allowed,
                await repository.ListExcludedPermissionIdsAsync(ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
        if (result.Excluded > 0 && result.Created == 0 && result.Skipped == 0 && result.OutOfCap == 0)
        {
            await RejectAsync(tenantId, actor, targetUserId, null, "MODULE_SNAPSHOT", PermissionOverrideReasons.Excluded, null, effect, ct, moduleCode)
                .ConfigureAwait(false);
        }

        if (result.OutOfCap > 0)
        {
            await repository.ApplyAsync(
                tenantId, actor.UserId, [], [],
                [new UserPermissionAuditEntry(
                    tenantId, actor.UserId, targetUserId, result.ProductCode, moduleCode, null, "MODULE_SNAPSHOT", "REJECTED",
                    PermissionOverrideReasons.OutOfCap, null, effect, result.BatchId)], ct).ConfigureAwait(false);
            if (result.Created == 0)
            {
                throw new PermissionOverrideRejectedException(PermissionOverrideReasons.OutOfCap, moduleCode);
            }
        }

        return result;
    }

    /// <summary>Overrides activos del usuario en el tenant, con el slug del permiso. Aplica las reglas de existencia y ámbito (AC7: leer también se valida).</summary>
    public async Task<IReadOnlyList<UserOverrideItem>> ListAsync(Guid tenantId, OverrideActor actor, Guid targetUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await RunRulesAsync(tenantId, actor, targetUserId, PermissionOverrideActions.Read, null, null, null, ct).ConfigureAwait(false);
        var items = new List<UserOverrideItem>();
        foreach (var o in await repository.ListActiveAsync(tenantId, targetUserId, ct).ConfigureAwait(false))
        {
            var p = await repository.GetPermissionAsync(o.PermissionId, ct).ConfigureAwait(false);
            items.Add(new UserOverrideItem(o.PermissionId, p?.Slug ?? o.PermissionId.ToString(), o.ProductCode, o.Effect, o.Source, o.GrantedBy));
        }

        return [.. items.OrderBy(i => i.Slug, StringComparer.Ordinal)];
    }

    /// <summary>
    /// HU #13646 (FD2-5): cuántos permisos individuales activos tiene el usuario en el tenant (y producto, si se indica), para avisar
    /// antes de cambiarle el rol. Mismas reglas de lectura que <see cref="ListAsync"/>: existencia y ámbito.
    /// </summary>
    public async Task<int> CountAsync(Guid tenantId, OverrideActor actor, Guid targetUserId, string? productCode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await RunRulesAsync(tenantId, actor, targetUserId, PermissionOverrideActions.Read, null, null, null, ct).ConfigureAwait(false);
        var active = await repository.ListActiveAsync(tenantId, targetUserId, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(productCode) ? active.Count : active.Count(o => o.ProductCode == productCode);
    }

    /// <summary>Catálogo otorgable (AC5): Super Admin = todo lo <c>enforced = 'true'</c>; el resto, solo su tope.</summary>
    public async Task<IReadOnlyList<PermissionCatalogEntry>> ListGrantableAsync(Guid tenantId, OverrideActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var all = await repository.ListEnforcedPermissionsAsync(ct).ConfigureAwait(false);
        var cap = await capCalculator.GetCapAsync(tenantId, actor, ct).ConfigureAwait(false);
        return [.. all.Where(p => cap.Contains(p.Id))];
    }

    private async Task<PermissionCatalogEntry> GetPermissionAsync(Guid permissionId, CancellationToken ct) =>
        await repository.GetPermissionAsync(permissionId, ct).ConfigureAwait(false)
            ?? throw new PermissionNotFoundForOverrideException(permissionId);

    private async Task<UserPermissionOverride?> FindActiveAsync(Guid tenantId, Guid userId, Guid permissionId, CancellationToken ct) =>
        (await repository.ListActiveAsync(tenantId, userId, ct).ConfigureAwait(false)).FirstOrDefault(o => o.PermissionId == permissionId);

    private async Task RunRulesAsync(
        Guid tenantId, OverrideActor actor, Guid targetUserId, string action, PermissionCatalogEntry? permission,
        string? before, string? after, CancellationToken ct, string? moduleCode = null)
    {
        var request = new PermissionOverrideRequest(tenantId, actor, targetUserId, action, permission?.Slug ?? moduleCode, permission?.Enforced);
        foreach (var rule in rules)
        {
            if (await rule.CheckAsync(request, ct).ConfigureAwait(false) is { } reason)
            {
                await RejectAsync(tenantId, actor, targetUserId, permission, action, reason, before, after, ct, moduleCode).ConfigureAwait(false);
            }
        }
    }

    private async Task RejectAsync(
        Guid tenantId, OverrideActor actor, Guid targetUserId, PermissionCatalogEntry? permission, string action,
        string reason, string? before, string? after, CancellationToken ct, string? moduleCode = null)
    {
        var entry = permission is null
            ? new UserPermissionAuditEntry(tenantId, actor.UserId, targetUserId, null, moduleCode, null, action, "REJECTED", reason, before, after, null)
            : Audit(tenantId, actor, targetUserId, permission, action, "REJECTED", reason, before, after);
        await repository.ApplyAsync(tenantId, actor.UserId, [], [], [entry], ct).ConfigureAwait(false);
        throw new PermissionOverrideRejectedException(reason, permission?.Slug ?? moduleCode ?? string.Empty);
    }

    private static UserPermissionAuditEntry Audit(
        Guid tenantId, OverrideActor actor, Guid targetUserId, PermissionCatalogEntry permission, string action,
        string outcome, string? reason, string? before, string? after) =>
        new(tenantId, actor.UserId, targetUserId, permission.ProductCode, permission.ModuleCode, permission.Slug,
            action, outcome, reason, before, after, null);
}
