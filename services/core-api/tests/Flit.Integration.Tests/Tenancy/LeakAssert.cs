using Xunit.Sdk;

namespace Flit.Integration.Tests.Tenancy;

/// <summary>
/// HU #12322 — aserción explícita de fuga entre clientes. Falla nombrando <b>cada</b> fila ajena
/// (id + tenant dueño + tenant que consulta) en lugar de un «expected 0 but was 1»: el revisor
/// sabe qué consulta fugó y de quién. <see cref="LeakDetectedException"/> es lo que se afirma en
/// <c>TenantLeakTests.El_helper_de_fuga_detecta_filas_ajenas</c> para demostrar que la suite no
/// está en verde por vacuidad.
/// </summary>
internal static class LeakAssert
{
    /// <summary>
    /// Ninguna fila de <paramref name="rows"/> puede pertenecer a un tenant fuera de
    /// <paramref name="allowed"/>. <paramref name="query"/> es el nombre canónico del inventario
    /// (<see cref="CoveredQueries"/>).
    /// </summary>
    public static void NoForeignRows<T>(
        string query,
        Guid reader,
        IReadOnlySet<Guid> allowed,
        IEnumerable<T> rows,
        Func<T, Guid> tenantOf,
        Func<T, object> idOf)
    {
        var foreign = rows
            .Where(r => !allowed.Contains(tenantOf(r)))
            .Select(r => $"  - fila {idOf(r)} del tenant {HierarchyScenario.CodeOf(tenantOf(r))} ({tenantOf(r)})")
            .ToList();

        if (foreign.Count == 0)
            return;

        throw new LeakDetectedException(
            $"FUGA en {query}: el lector {HierarchyScenario.CodeOf(reader)} ({reader}) obtuvo {foreign.Count} fila(s) ajena(s) " +
            $"(permitidos: {string.Join(", ", allowed.Select(HierarchyScenario.CodeOf))}):\n{string.Join("\n", foreign)}");
    }

    /// <summary>Variante para lecturas que devuelven ids de trámite sin el tenant (se resuelve por la semilla).</summary>
    public static void NoForeignProcedures(string query, Guid reader, IReadOnlySet<Guid> allowed, IEnumerable<Guid> procedureIds) =>
        NoForeignRows(query, reader, allowed, procedureIds, OwnerOfProcedure, id => id);

    /// <summary>
    /// Para consultas agregadas (conteos, métricas) donde no hay filas con tenant: el valor obtenido
    /// leyendo como <paramref name="reader"/> debe ser exactamente el propio; si coincide con el
    /// total del escenario, la consulta ignoró el filtro.
    /// </summary>
    public static void OwnAggregateOnly(string query, Guid reader, long obtained, long own, long totalScenario)
    {
        if (obtained == own)
            return;

        throw new LeakDetectedException(
            $"FUGA en {query}: el lector {HierarchyScenario.CodeOf(reader)} ({reader}) obtuvo {obtained} " +
            $"cuando lo propio es {own} (total del escenario sin filtro: {totalScenario}).");
    }

    private static Guid OwnerOfProcedure(Guid procedureId) =>
        HierarchyScenario.Clients.FirstOrDefault(t => HierarchyScenario.ProceduresOf(t).Contains(procedureId));
}

/// <summary>Fallo de la suite negativa: una lectura devolvió filas de otro cliente.</summary>
public sealed class LeakDetectedException(string message) : XunitException(message);
