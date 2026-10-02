namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Bug #13194 (review PR #510, MAYOR-2) — ejecuta una unidad de trabajo dentro de un SAVEPOINT de la
/// transacción en curso, si la hay. Si la operación falla, revierte solo hasta el savepoint (la transacción
/// del llamador sigue utilizable) y relanza. Sin transacción en curso ejecuta la operación tal cual.
/// <para>Lo usa el consumidor del outbox de identidad para que el error de UN trámite del lote no aborte la
/// transacción del outbox ni corte el resto del lote.</para>
/// </summary>
public interface ISavepointScope
{
    Task<T> EjecutarAsync<T>(Func<Task<T>> operacion, CancellationToken ct = default);
}
