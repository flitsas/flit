using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Bug #13194 (review PR #510, L3/MENOR-2/MAYOR-2) — piezas comunes para ejecutar trabajo dentro de un
/// SAVEPOINT de la transacción ambiente: revertir sin perder la excepción original y desacoplar del change
/// tracker las entidades que la operación fallida dejó rastreadas (sus filas ya no existen tras revertir).
/// Vive en Flit.Identity.Infrastructure (mismo namespace) porque lo usa <c>TenantRlsScope</c>, compartido con
/// core-identity (Epic #13217).
/// </summary>
internal static class SavepointEf
{
    /// <summary>Nombre único por llamada (tolera ámbitos anidados).</summary>
    public static string NuevoNombre(string prefijo) => prefijo + "_" + Guid.NewGuid().ToString("N");

    /// <summary>Entidades rastreadas ANTES de la operación (por referencia).</summary>
    public static HashSet<object> Rastreadas(DbContext context) =>
        context.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Revierte al savepoint y desacopla las entidades que no estaban rastreadas antes (insertadas o
    /// cargadas por la operación fallida). Si el rollback falla, lanza <see cref="AggregateException"/> con
    /// la excepción ORIGINAL primero, para no perderla. Siempre con <see cref="CancellationToken.None"/>:
    /// una cancelación no puede dejar la transacción ambiente a medio revertir.
    /// </summary>
    public static async Task RevertirAsync(
        DbContext context,
        IDbContextTransaction transaccion,
        string savepoint,
        HashSet<object> previas,
        Exception original)
    {
        try
        {
            await transaccion.RollbackToSavepointAsync(savepoint, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception rollback)
        {
            throw new AggregateException(
                "La operación falló y además no se pudo revertir al savepoint.", original, rollback);
        }

        DesacoplarNuevas(context, previas);
    }

    /// <summary>
    /// Desacopla del change tracker toda entidad que no estaba rastreada antes de la operación. Las
    /// preexistentes conservan su estado (si la operación las modificó y guardó, la base ya volvió atrás;
    /// el llamador no debe reutilizarlas sin recargar).
    /// </summary>
    public static void DesacoplarNuevas(DbContext context, HashSet<object> previas)
    {
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (!previas.Contains(entry.Entity))
                entry.State = EntityState.Detached;
        }
    }
}
