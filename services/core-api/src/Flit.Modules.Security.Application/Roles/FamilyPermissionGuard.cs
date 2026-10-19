using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

/// <summary>
/// HU #13664 AC1 — aplica <see cref="FamilyPermissionRules"/> al juego de permisos de un rol: «Crear» sin «Ver» lanza
/// <see cref="FamilyCreateRequiresReadException"/> y quitar «Ver» retira también su «Crear». Sin política activa devuelve el juego intacto.
/// </summary>
internal static class FamilyPermissionGuard
{
    public static async Task<IReadOnlyList<Guid>> ApplyAsync(
        IRoleRepository repository,
        IFamilyPermissionRulePolicy? policy,
        IEnumerable<string> currentSlugs,
        IReadOnlyList<Guid> requestedIds,
        CancellationToken ct)
    {
        if (policy is not { Enabled: true } || requestedIds.Count == 0)
            return requestedIds;

        var infos = await repository.GetPermissionInfosAsync(requestedIds, ct);
        var requested = infos.Select(i => i.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolution = FamilyPermissionRules.Resolve(currentSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase), requested);
        if (resolution.Violations.Count > 0)
            throw new FamilyCreateRequiresReadException(resolution.Violations);
        if (resolution.CascadeDropped.Count == 0)
            return requestedIds;

        var dropped = infos.Where(i => resolution.CascadeDropped.Contains(i.Slug, StringComparer.OrdinalIgnoreCase)).Select(i => i.Id).ToHashSet();
        return [.. requestedIds.Where(id => !dropped.Contains(id))];
    }
}
