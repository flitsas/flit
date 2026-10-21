namespace Flit.Modules.Security.Domain.PermissionOverrides;

/// <summary>Permiso individual vigente de un usuario, tal como entra al cálculo de permisos efectivos.</summary>
public sealed record EffectivePermissionOverride(string Slug, string ProductCode, string Effect);

/// <summary>
/// Calculador ÚNICO de los permisos efectivos del token (HU #13643, ADR-0073, FB-3: producto &gt; usuario &gt; rol).
/// Lo usan el JWT heredado (<c>AuthUserRepository</c> → <c>RsaJwtTokenIssuer</c>) y el token OIDC
/// (<c>OidcPrincipalFactory</c>), así el claim <c>permissions</c> es el mismo por las dos rutas.
/// </summary>
public static class EffectivePermissionsCalculator
{
    /// <summary>
    /// <c>rol + allow − deny</c>. Un <c>allow</c> solo suma si <paramref name="isProductEnabled"/> acepta su producto;
    /// un <c>deny</c> siempre resta. Sin overrides devuelve los permisos del rol tal cual (mismo orden).
    /// </summary>
    public static IReadOnlyList<string> Compute(
        IReadOnlyList<string> rolePermissions,
        IReadOnlyList<EffectivePermissionOverride> overrides,
        Func<string, bool> isProductEnabled)
    {
        ArgumentNullException.ThrowIfNull(rolePermissions);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(isProductEnabled);
        if (overrides.Count == 0)
        {
            return rolePermissions;
        }

        var denied = overrides
            .Where(o => o.Effect == PermissionEffects.Deny)
            .Select(o => o.Slug)
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<string>(rolePermissions.Count + overrides.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slug in rolePermissions)
        {
            if (!denied.Contains(slug) && seen.Add(slug))
            {
                result.Add(slug);
            }
        }

        foreach (var o in overrides
                     .Where(o => o.Effect == PermissionEffects.Allow && isProductEnabled(o.ProductCode))
                     .OrderBy(o => o.Slug, StringComparer.Ordinal))
        {
            if (!denied.Contains(o.Slug) && seen.Add(o.Slug))
            {
                result.Add(o.Slug);
            }
        }

        return result;
    }
}
