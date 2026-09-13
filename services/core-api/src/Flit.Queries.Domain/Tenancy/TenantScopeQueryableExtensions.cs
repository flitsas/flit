using System.Linq.Expressions;
using System.Reflection;

namespace Flit.Queries.Domain.Tenancy;

/// <summary>
/// Filtro de lectura por <see cref="TenantScope"/> sobre <see cref="IQueryable{T}"/> (HU #12321).
/// Cerrado por defecto: solo <see cref="TenantScope.IsAll"/> deja la consulta sin filtro; un conjunto
/// de lectura vacío (defensivo, no debería ocurrir fuera de <c>All</c>) devuelve cero filas, NUNCA
/// "sin filtro".
/// </summary>
public static class TenantScopeQueryableExtensions
{
    /// <summary>
    /// Restringe <paramref name="query"/> a las filas cuyo tenant (según <paramref name="tenantSelector"/>)
    /// esté dentro de <see cref="TenantScope.ReadTenantIds"/>. Traducible por EF Core
    /// (<c>List&lt;Guid&gt;.Contains</c> sobre una lista materializada — método de instancia no
    /// genérico, sin <c>MakeGenericMethod</c>: compatible con AOT).
    /// </summary>
    public static IQueryable<T> WhereTenantInScope<T>(
        this IQueryable<T> query,
        TenantScope scope,
        Expression<Func<T, Guid>> tenantSelector)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(tenantSelector);

        if (scope.IsAll)
            return query;

        if (scope.ReadTenantIds.Count == 0)
            return query.Where(_ => false);

        var ids = scope.ReadTenantIds.ToList();
        var parameter = tenantSelector.Parameters[0];
        var contains = Expression.Call(Expression.Constant(ids), ListContains, tenantSelector.Body);
        var predicate = Expression.Lambda<Func<T, bool>>(contains, parameter);
        return query.Where(predicate);
    }

    private static readonly MethodInfo ListContains =
        typeof(List<Guid>).GetMethod(nameof(List<Guid>.Contains), [typeof(Guid)])
        ?? throw new InvalidOperationException("List<Guid>.Contains(Guid) no encontrado.");
}
