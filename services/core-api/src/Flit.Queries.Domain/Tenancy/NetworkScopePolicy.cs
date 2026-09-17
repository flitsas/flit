namespace Flit.Queries.Domain.Tenancy;

/// <summary>
/// HU #12358 (Feature #12257, épica #12235) — política de las rutas consolidadas de la red
/// (<c>/api/v1/tramites/network/**</c>): solo una cabeza de grupo con hijos tiene «red». Un SuperAdmin
/// (<see cref="TenantScope.IsAll"/>) tiene el listado global por sus rutas de siempre; un cliente sin
/// jerarquía (<c>Single</c>) no tiene nada que consolidar. Ninguno de los dos entra por aquí.
/// <para>
/// Códigos de error (contrato con la API): <see cref="ScopeRequired"/> ⇒ 403;
/// <see cref="RoleRequired"/> ⇒ 403 (HU #12652: la cabeza tiene red pero el actor no es su
/// administrador); <see cref="ChildOutOfScope"/> ⇒ 403 SIN ejecutar la consulta (el hijo pedido no
/// está en el conjunto de lectura; jamás se «amplía» el alcance para complacer al filtro).
/// </para>
/// <para>
/// HU #12652 (Feature #12257): la red es exclusiva del rol <see cref="HeadAdminRole"/> de la cabeza.
/// Se evalúa DESPUÉS del alcance, así que un hijo, un cliente sin red o un SuperAdmin siguen
/// recibiendo <see cref="ScopeRequired"/> (sin regresión) y solo un usuario de una cabeza con red
/// pero sin el rol recibe <see cref="RoleRequired"/>. Multi-rol: basta con que UNO de los roles
/// activos sea el administrador. Aplica por igual a cabezas CONCESION y MARCA_BLANCA.
/// </para>
/// <para>
/// HU #12359: vive en el dominio compartido de tenancy (no en <c>Flit.Tramites.Application</c>) porque
/// la misma regla acota el listado consolidado (trámites) y las estadísticas de red (analítica) sin
/// acoplar un módulo de aplicación al otro. La regla es pura y no cambió.
/// </para>
/// </summary>
public static class NetworkScopePolicy
{
    public const string ScopeRequired = "network_scope_required";
    public const string ChildOutOfScope = "network_child_out_of_scope";
    public const string RoleRequired = "network_role_required";

    /// <summary>Único rol que lee la red de su cabeza (HU #12652). Mismo código que emite el JWT FLIT.</summary>
    public const string HeadAdminRole = "AdminCompany";

    /// <summary><c>null</c> si <paramref name="scope"/> es una cabeza de grupo; si no, el código de error.</summary>
    public static string? Validate(TenantScope? scope) =>
        scope is null || scope.IsAll || !scope.IsGroup ? ScopeRequired : null;

    /// <summary>
    /// <c>null</c> si alguno de los <paramref name="roles"/> activos del actor es <see cref="HeadAdminRole"/>
    /// (comparación sin distinguir mayúsculas, como el resto de policies de rol); si no,
    /// <see cref="RoleRequired"/>. No mira la base: el alcance ya garantizó que el actor es cabeza.
    /// </summary>
    public static string? ValidateRole(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Any(r => string.Equals(r, HeadAdminRole, StringComparison.OrdinalIgnoreCase))
            ? null
            : RoleRequired;
    }

    /// <summary>
    /// Acota el alcance al cliente <paramref name="childTenantId"/> (que puede ser la propia cabeza).
    /// Sin filtro devuelve el alcance intacto. Con un cliente fuera de <see cref="TenantScope.ReadTenantIds"/>
    /// devuelve <see cref="ChildOutOfScope"/>: la decisión se toma en memoria, sin tocar la base.
    /// </summary>
    public static (TenantScope? Scope, string? Error) Narrow(TenantScope scope, Guid? childTenantId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (childTenantId is not { } child || child == Guid.Empty)
            return (scope, null);

        return scope.ReadTenantIds.Contains(child)
            ? (TenantScope.Single(child), null)
            : (null, ChildOutOfScope);
    }
}
