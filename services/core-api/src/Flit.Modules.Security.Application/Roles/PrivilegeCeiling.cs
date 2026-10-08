using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

/// <summary>
/// HU #13441 AC3 — tope de escalada: el Admin de Compañía solo otorga permisos que él mismo posee, de módulos de
/// productos habilitados para su tenant, y nunca permisos de plataforma (p. ej. <c>banners.manage</c>).
/// Orden de reporte: desconocido, plataforma, módulo no habilitado, no poseído (el primero que aplique decide el código).
/// </summary>
public static class PrivilegeCeiling
{
    /// <summary>Permisos que solo gestiona FLIT: ninguna compañía puede ponerlos en un rol propio.</summary>
    public static bool IsPlatformOnly(string slug) =>
        slug.StartsWith("banners.", StringComparison.OrdinalIgnoreCase)
        || slug.StartsWith("superadmin.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lanza <see cref="PrivilegeCeilingException"/> si algún permiso pedido excede el techo.</summary>
    public static void Enforce(
        IReadOnlyList<Guid> requestedIds,
        IReadOnlyList<PermissionInfo> infos,
        IReadOnlyCollection<string> callerPermissions,
        IReadOnlySet<string> enabledProducts)
    {
        var known = infos.Select(i => i.Id).ToHashSet();
        var unknown = requestedIds.Where(id => !known.Contains(id)).Select(id => id.ToString()).ToList();
        if (unknown.Count > 0)
            throw new PrivilegeCeilingException(PrivilegeCeilingCodes.Unknown, unknown);

        var platform = infos.Where(i => IsPlatformOnly(i.Slug)).Select(i => i.Slug).ToList();
        if (platform.Count > 0)
            throw new PrivilegeCeilingException(PrivilegeCeilingCodes.PlatformOnly, platform);

        var notEnabled = infos.Where(i => !enabledProducts.Contains(i.ProductCode)).Select(i => i.Slug).ToList();
        if (notEnabled.Count > 0)
            throw new PrivilegeCeilingException(PrivilegeCeilingCodes.ModuleNotEnabled, notEnabled);

        var held = callerPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notHeld = infos.Where(i => !held.Contains(i.Slug)).Select(i => i.Slug).ToList();
        if (notHeld.Count > 0)
            throw new PrivilegeCeilingException(PrivilegeCeilingCodes.NotHeld, notHeld);
    }
}
