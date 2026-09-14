namespace Flit.Queries.Domain.Tenancy;

/// <summary>
/// HU #12358 (Feature #12257, épica #12235) — política de las rutas consolidadas de la red
/// (<c>/api/v1/tramites/network/**</c>): solo una cabeza de grupo con hijos tiene «red». Un SuperAdmin
/// (<see cref="TenantScope.IsAll"/>) tiene el listado global por sus rutas de siempre; un cliente sin
/// jerarquía (<c>Single</c>) no tiene nada que consolidar. Ninguno de los dos entra por aquí.
/// <para>
/// Códigos de error (contrato con la API): <see cref="ScopeRequired"/> ⇒ 403;
/// <see cref="ChildOutOfScope"/> ⇒ 403 SIN ejecutar la consulta (el hijo pedido no está en el
/// conjunto de lectura; jamás se «amplía» el alcance para complacer al filtro).
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

    /// <summary><c>null</c> si <paramref name="scope"/> es una cabeza de grupo; si no, el código de error.</summary>
    public static string? Validate(TenantScope? scope) =>
        scope is null || scope.IsAll || !scope.IsGroup ? ScopeRequired : null;

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
