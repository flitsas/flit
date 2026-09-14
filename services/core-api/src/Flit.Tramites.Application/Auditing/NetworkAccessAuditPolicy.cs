using Flit.Queries.Domain.Tenancy;

namespace Flit.Tramites.Application.Auditing;

/// <summary>
/// HU #12361 — regla pura de «a qué hijos alcanzó la petición» (AC1/AC5/AC8), compartida por el
/// endpoint filter de la API y por las pruebas de integración:
/// <list type="bullet">
///   <item>Solo una cabeza de grupo (<see cref="TenantScope.IsGroup"/> con <see cref="TenantScope.WriteTenantId"/>)
///   genera auditoría; SuperAdmin (<c>All</c>) y clientes sin red (<c>Single</c>) devuelven vacío (AC6).</item>
///   <item>La propia cabeza nunca cuenta como alcanzada (AC5): un resultado solo con datos propios ⇒ vacío.</item>
///   <item>Con <c>result = ok</c> solo cuentan los hijos del alcance de lectura (nada ajeno puede colarse);
///   con un rechazo (<c>forbidden</c>/<c>not_found</c>) se conserva el hijo pedido aunque esté fuera del
///   alcance: es justamente el intento que hay que registrar (AC7).</item>
///   <item>Distintos y sin <see cref="Guid.Empty"/>: trescientos trámites de dos hijos ⇒ dos ids (AC8).</item>
/// </list>
/// </summary>
public static class NetworkAccessAuditPolicy
{
    public static IReadOnlyList<Guid> ReachedChildren(TenantScope? scope, IEnumerable<Guid> presentTenantIds, string result)
    {
        ArgumentNullException.ThrowIfNull(presentTenantIds);

        if (scope is null || !scope.IsGroup || scope.WriteTenantId is not { } head)
            return [];

        var allowForeign = !string.Equals(result, NetworkAccessVocabulary.Results.Ok, StringComparison.Ordinal);

        return presentTenantIds
            .Where(t => t != Guid.Empty && t != head && (allowForeign || scope.ReadTenantIds.Contains(t)))
            .Distinct()
            .ToList();
    }
}
