using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.PermissionOverrides;

/// <summary><c>Changed</c> = false cuando el usuario ya tenía exactamente ese efecto (idempotente, sin bitácora).</summary>
public sealed record SetOverrideResult(Guid PermissionId, string Effect, bool Changed);

/// <remarks>HU #13655: <c>Name</c> y <c>ModuleCode</c> permiten mostrar un permiso fuera del tope de quien consulta, que no viene en su
/// catálogo de módulos.</remarks>
public sealed record UserOverrideItem(
    Guid PermissionId, string Slug, string ProductCode, string Effect, string Source, Guid GrantedBy, string? Name = null, string? ModuleCode = null);

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
    IEnumerable<IPermissionOverrideRule> rules,
    IPermissionEnforcementState? enforcement = null)
{
    // Hallazgo review Épica #12753: sin estado de enforcement (null) todo permiso enforced='true' se considera exigido.
    private bool IsLive(string slug) => enforcement?.IsEffectivelyEnforced(slug) ?? true;

    public async Task<SetOverrideResult> SetAsync(
        Guid tenantId, OverrideActor actor, Guid targetUserId, Guid permissionId, string effect, CancellationToken ct, bool piiConfirmed = false)
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

        // Allow o deny sobre un permiso cuyo flag está apagado no tiene efecto real (la API lo deja pasar): falsa revocación.
        // Quitar el override (RemoveAsync) sí se permite siempre, para poder limpiar.
        if (permission.IsEnforced && !IsLive(permission.Slug))
        {
            await RejectAsync(tenantId, actor, targetUserId, permission, action, PermissionOverrideReasons.NotEnforced, existing?.Effect, effect, ct)
                .ConfigureAwait(false);
        }

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

        // HU #13653 (FB-17): habilitar ict.pii.reveal exige la confirmación Ley 1581; sin ella, 400 y nada cambia.
        var needsPii = effect == PermissionEffects.Allow && permission.Slug == PermissionAuditActions.PiiRevealSlug;
        if (needsPii && !piiConfirmed)
        {
            throw new PiiConfirmationRequiredException();
        }

        // HU #13666: «crear exige ver» sobre el permiso EFECTIVO (rol + individuales).
        var family = IsFamilyPermission(permission.Slug)
            ? await LoadFamilyAsync(tenantId, targetUserId, ct).ConfigureAwait(false)
            : null;
        if (family is not null && effect == PermissionEffects.Allow && IsFamilyCreate(permission.Slug))
        {
            var after = family.Effective(null);
            after.Add(permission.Slug);
            if (FamilyPermissionRules.Resolve(family.Effective(null), after).Violations.Contains(permission.Slug))
            {
                await RejectAsync(tenantId, actor, targetUserId, permission, action, PermissionOverrideReasons.FamilyCreateRequiresRead, existing?.Effect, effect, ct)
                    .ConfigureAwait(false);
            }
        }

        if (existing?.Effect == effect)
        {
            return new SetOverrideResult(permissionId, effect, false);
        }

        var removeIds = new List<Guid>();
        var adds = new List<UserPermissionOverride>();
        var audits = new List<UserPermissionAuditEntry>();
        if (existing is not null)
        {
            removeIds.Add(existing.Id);
        }

        adds.Add(new UserPermissionOverride(
            Guid.NewGuid(), tenantId, targetUserId, permission.ProductCode, permissionId, effect,
            PermissionOverrideSources.Individual, null, actor.UserId));
        audits.Add(Audit(tenantId, actor, targetUserId, permission, action, "OK", null, existing?.Effect, effect));

        if (needsPii)
        {
            // Misma transacción que el GRANT: o quedan ambas filas o ninguna.
            audits.Add(Audit(tenantId, actor, targetUserId, permission, PermissionAuditActions.PiiRevealConfirmed, "OK", null, existing?.Effect, effect));
        }

        // Denegar «Ver» apaga el «Crear» de la familia: se retira el allow individual y, si el rol lo da, se deniega.
        if (family is not null && effect == PermissionEffects.Deny && IsFamilyRead(permission.Slug))
        {
            var before = family.Effective(null);
            var afterSet = family.Effective(null);
            afterSet.Remove(permission.Slug);
            AddCascade(tenantId, actor, targetUserId, family, FamilyPermissionRules.Resolve(before, afterSet).CascadeDropped, removeIds, adds, audits);
        }

        await repository.ApplyAsync(tenantId, actor.UserId, removeIds, adds, audits, ct).ConfigureAwait(false);
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

        var removeIds = new List<Guid> { existing.Id };
        var adds = new List<UserPermissionOverride>();
        var audits = new List<UserPermissionAuditEntry> { Audit(tenantId, actor, targetUserId, permission, "REVOKE", "OK", null, existing.Effect, null) };

        // HU #13666: quitar un allow individual de «Ver» devuelve «Ver» al rol; si el rol no lo da, el «Crear» se arrastra.
        if (existing.Effect == PermissionEffects.Allow && IsFamilyRead(permission.Slug))
        {
            var family = await LoadFamilyAsync(tenantId, targetUserId, ct).ConfigureAwait(false);
            var before = family.Effective(null);
            var afterSet = family.Effective(permission.Slug);
            AddCascade(tenantId, actor, targetUserId, family, FamilyPermissionRules.Resolve(before, afterSet).CascadeDropped, removeIds, adds, audits);
        }

        await repository.ApplyAsync(tenantId, actor.UserId, removeIds, adds, audits, ct).ConfigureAwait(false);
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

        var excluded = (await repository.ListExcludedPermissionIdsAsync(ct).ConfigureAwait(false)).ToHashSet();
        if (effect == PermissionEffects.Allow
            && await repository.GetModuleActionsAsync(moduleCode, ct).ConfigureAwait(false) is { } snapshot)
        {
            // HU #13653: ict.pii.reveal exige confirmación Ley 1581 permiso a permiso; no entra al «módulo completo».
            excluded.UnionWith(snapshot.Actions.Where(a => a.Slug == PermissionAuditActions.PiiRevealSlug).Select(a => a.PermissionId));
        }

        // Acciones enforced='true' cuyo flag está apagado: no se otorgan ni se deniegan (falsa revocación).
        IReadOnlySet<Guid>? notEnforced = null;
        if (enforcement is not null)
        {
            notEnforced = (await repository.ListEnforcedPermissionsAsync(ct).ConfigureAwait(false))
                .Where(p => !IsLive(p.Slug)).Select(p => p.Id).ToHashSet();
        }

        var result = await moduleHandler.HandleAsync(
            new GrantModuleOverridesCommand(
                tenantId, targetUserId, actor.UserId, moduleCode, effect, allowed, excluded, notEnforced), ct).ConfigureAwait(false);
        if (result.Excluded > 0 && result.Created == 0 && result.Skipped == 0 && result.OutOfCap == 0)
        {
            await RejectAsync(tenantId, actor, targetUserId, null, "MODULE_SNAPSHOT", PermissionOverrideReasons.Excluded, null, effect, ct, moduleCode)
                .ConfigureAwait(false);
        }

        // Todo lo que habría tocado está apagado: 403 NOT_ENFORCED auditado. Si queda algo exigido, se aplica solo eso.
        if (result.NotEnforced > 0 && result.Created == 0 && result.Skipped == 0 && result.OutOfCap == 0 && result.Excluded == 0)
        {
            await RejectAsync(tenantId, actor, targetUserId, null, "MODULE_SNAPSHOT", PermissionOverrideReasons.NotEnforced, null, effect, ct, moduleCode)
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
            items.Add(new UserOverrideItem(o.PermissionId, p?.Slug ?? o.PermissionId.ToString(), o.ProductCode, o.Effect, o.Source, o.GrantedBy, p?.Name, p?.ModuleCode));
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
        // Los permisos cuyo flag de enforcement está apagado no se ofrecen: otorgarlos o denegarlos no tendría efecto en la API.
        return [.. all.Where(p => cap.Contains(p.Id) && IsLive(p.Slug))];
    }

    // ── HU #13666: regla «crear exige ver» por familia sobre permisos individuales ─────────────────────────────

    private static bool IsFamilyPermission(string slug) => IsFamilyRead(slug) || IsFamilyCreate(slug);

    private static bool IsFamilyRead(string slug) => FamilyPermissionRules.Pairs.Any(p => p.Read == slug);

    private static bool IsFamilyCreate(string slug) => FamilyPermissionRules.Pairs.Any(p => p.Create == slug);

    private async Task<FamilyState> LoadFamilyAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var slugs = FamilyPermissionRules.Pairs.SelectMany(p => new[] { p.Read, p.Create }).ToHashSet(StringComparer.Ordinal);
        var catalog = (await repository.ListEnforcedPermissionsAsync(ct).ConfigureAwait(false))
            .Where(p => slugs.Contains(p.Slug)).ToDictionary(p => p.Slug, StringComparer.Ordinal);
        var roleIds = await repository.GetRolePermissionIdsAsync(tenantId, userId, ct).ConfigureAwait(false);
        var overrides = await repository.ListActiveAsync(tenantId, userId, ct).ConfigureAwait(false);
        return new FamilyState(catalog, roleIds, overrides);
    }

    /// <summary>Retira el «Crear» arrastrado: borra su allow individual y, si el rol lo da, lo deniega (queda apagado).</summary>
    private static void AddCascade(
        Guid tenantId, OverrideActor actor, Guid targetUserId, FamilyState family, IReadOnlyList<string> createSlugs,
        List<Guid> removeIds, List<UserPermissionOverride> adds, List<UserPermissionAuditEntry> audits)
    {
        foreach (var slug in createSlugs)
        {
            if (!family.Catalog.TryGetValue(slug, out var create))
            {
                continue;
            }

            var ov = family.Overrides.FirstOrDefault(o => o.PermissionId == create.Id);
            if (ov is not null && !removeIds.Contains(ov.Id))
            {
                removeIds.Add(ov.Id);
            }

            if (family.RoleIds.Contains(create.Id))
            {
                adds.Add(new UserPermissionOverride(
                    Guid.NewGuid(), tenantId, targetUserId, create.ProductCode, create.Id, PermissionEffects.Deny,
                    PermissionOverrideSources.Individual, null, actor.UserId));
                audits.Add(Audit(tenantId, actor, targetUserId, create, "DENY", "OK", null, ov?.Effect, PermissionEffects.Deny));
            }
            else if (ov is not null)
            {
                audits.Add(Audit(tenantId, actor, targetUserId, create, "REVOKE", "OK", null, ov.Effect, null));
            }
        }
    }

    private sealed record FamilyState(
        IReadOnlyDictionary<string, PermissionCatalogEntry> Catalog,
        IReadOnlySet<Guid> RoleIds,
        IReadOnlyList<UserPermissionOverride> Overrides)
    {
        /// <summary>
        /// Slugs de familia que el usuario tiene hoy (rol + individuales; deny gana). Con <paramref name="ignoreOverrideOf"/>,
        /// el override de ESE slug se ignora (simula quitarlo y volver al rol).
        /// </summary>
        public HashSet<string> Effective(string? ignoreOverrideOf)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (slug, entry) in Catalog)
            {
                var ov = slug == ignoreOverrideOf ? null : Overrides.FirstOrDefault(o => o.PermissionId == entry.Id);
                var on = ov is null ? RoleIds.Contains(entry.Id) : ov.Effect == PermissionEffects.Allow;
                if (on)
                {
                    set.Add(slug);
                }
            }

            return set;
        }
    }

    /// <summary>
    /// HU #13653, regla «Solo SA»: de los permisos otorgables, los que NINGÚN administrador del tenant destino (AdminCompany,
    /// ot_admin, admin_tramites) tiene en su tope (<see cref="PermissionCapCalculator"/>: roles menos sus deny). Un tenant sin
    /// administradores deja todo como «Solo SA».
    /// </summary>
    public async Task<IReadOnlySet<Guid>> ListSuperAdminOnlyAsync(
        Guid tenantId, IReadOnlyList<PermissionCatalogEntry> grantable, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grantable);
        var caps = new List<PermissionCap>();
        foreach (var adminId in await repository.ListTenantAdministratorIdsAsync(tenantId, ct).ConfigureAwait(false))
        {
            caps.Add(await capCalculator.GetCapAsync(tenantId, new OverrideActor(adminId, false), ct).ConfigureAwait(false));
        }

        return grantable.Where(p => !caps.Exists(c => c.Contains(p.Id))).Select(p => p.Id).ToHashSet();
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
