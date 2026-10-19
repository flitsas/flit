namespace Flit.Modules.Security.Domain.Roles;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) — regla «crear exige ver» de los permisos por familia de trámite
/// (<c>matriculas</c>, <c>traspasos</c>, <c>otros-tramites</c>). Función pura y reutilizable: la usa la configuración de
/// roles (HU #13664) y la debe llamar la configuración por usuario del Feature F-B (#13101) sobre su propio juego de slugs.
/// <para>
/// Uso de ejemplo: <c>var r = FamilyPermissionRules.Resolve(actuales, pedidos); if (r.Violations.Count &gt; 0) → 400;
/// pedidos.ExceptWith(r.CascadeDropped);</c>
/// </para>
/// </summary>
public static class FamilyPermissionRules
{
    /// <summary>Pares (Ver, Crear) por familia; deben coincidir con <c>FamilyPermissionSlugs</c> de Tramites.Domain (lo verifica un test).</summary>
    public static readonly IReadOnlyList<(string Read, string Create)> Pairs =
    [
        ("matriculas.read", "matriculas.create"),
        ("traspasos.read", "traspasos.create"),
        ("otros-tramites.read", "otros-tramites.create"),
    ];

    /// <summary>
    /// <paramref name="current"/> = slugs que la entidad (rol/usuario) tiene hoy; <paramref name="requested"/> = juego completo pedido.
    /// <list type="bullet">
    /// <item><b>Violations</b>: «Crear» pedido sin su «Ver» (otorgarlo solo) → el llamador responde 400.</item>
    /// <item><b>CascadeDropped</b>: «Crear» que se arrastra porque se quitó su «Ver» (lo tenía y ya no) → el llamador lo retira.</item>
    /// </list>
    /// </summary>
    public static FamilyPermissionResolution Resolve(IReadOnlySet<string> current, IReadOnlySet<string> requested)
    {
        var violations = new List<string>();
        var cascade = new List<string>();
        foreach (var (read, create) in Pairs)
        {
            if (!requested.Contains(create) || requested.Contains(read))
                continue;

            if (current.Contains(read))
                cascade.Add(create);
            else
                violations.Add(create);
        }

        return new FamilyPermissionResolution(violations, cascade);
    }
}

/// <summary>Resultado de <see cref="FamilyPermissionRules.Resolve"/>.</summary>
public sealed record FamilyPermissionResolution(IReadOnlyList<string> Violations, IReadOnlyList<string> CascadeDropped);

/// <summary>Interruptor de la regla «crear exige ver» (detrás de <c>Security:ModulePermissionEnforcement:Enabled</c>).</summary>
public interface IFamilyPermissionRulePolicy
{
    bool Enabled { get; }
}

/// <summary>HU #13664 AC1: se pidió un «Crear» por familia sin su «Ver» → 400 <c>FAMILY_CREATE_REQUIRES_READ</c>.</summary>
public sealed class FamilyCreateRequiresReadException(IReadOnlyList<string> slugs)
    : Exception($"Create permissions require their read permission: {string.Join(", ", slugs)}")
{
    public IReadOnlyList<string> Slugs { get; } = slugs;
}
