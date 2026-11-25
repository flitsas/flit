using Flit.Impuestos.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// Escritura serializada de la fila de <c>impuestos.aprovisionamientos</c> de una compañía (code review IMP-4 / DB B8). Quien cambia
/// el aprovisionamiento lee la fila con <c>SELECT … FOR UPDATE</c> dentro de su transacción, así que lee lo último confirmado y
/// nadie lo pisa hasta el commit; el token <c>xmin</c> queda de red para quien no la bloquee (un guardado sobre una lectura vieja
/// falla con <see cref="DbUpdateConcurrencyException"/>).
/// <para>
/// Orden de bloqueos: primero la fila de <c>aprovisionamientos</c> y después la de <c>credenciales_empresa</c> de la misma compañía.
/// Ningún camino toma la credencial y luego el aprovisionamiento. Las transiciones atómicas por <c>ExecuteUpdate</c> condicionado
/// (reserva del <see cref="Aprovisionador"/>, firma del webhook en <c>ProcesadorAviso</c>) toman solo la fila y respetan el orden.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Dentro de una transacción ya abierta (consumidor del SDK):
/// await BloqueoAprovisionamiento.BloquearAsync(db, empresa, ct);
/// // Con transacción propia y reintento ante concurrencia:
/// await BloqueoAprovisionamiento.EnTransaccionAsync(db, empresa, async ct =&gt; { /* leer, cambiar, SaveChanges */ return true; }, ct);
/// </code>
/// </example>
internal static class BloqueoAprovisionamiento
{
    /// <summary>Intentos ante <see cref="DbUpdateConcurrencyException"/> en <see cref="EnTransaccionAsync{T}"/>.</summary>
    public const int Intentos = 3;

    /// <summary>Bloquea la fila de la compañía hasta el fin de la transacción en curso (que debe existir).</summary>
    public static async Task BloquearAsync(ImpuestosDb db, Guid empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("El bloqueo del aprovisionamiento va dentro de una transacción (se suelta con ella).");
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM impuestos.aprovisionamientos WHERE tenant_id = {empresa} FOR UPDATE", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ejecuta <paramref name="paso"/> (que lee, cambia y guarda) en una transacción propia con la fila bloqueada y la confirma. Si
    /// el guardado choca con otra escritura, limpia el contexto y vuelve a empezar (relectura y reaplicación), hasta
    /// <see cref="Intentos"/> veces.
    /// </summary>
    public static async Task<T> EnTransaccionAsync<T>(ImpuestosDb db, Guid empresa, Func<CancellationToken, Task<T>> paso, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(paso);
        for (var intento = 1; ; intento++)
        {
            try
            {
                return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                    await BloquearAsync(db, empresa, ct).ConfigureAwait(false);
                    var resultado = await paso(ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    return resultado;
                }).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException) when (intento < Intentos)
            {
                // Otra escritura confirmó primero: se relee y se vuelve a aplicar.
            }
        }
    }
}
