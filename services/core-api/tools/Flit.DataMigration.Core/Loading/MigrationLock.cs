using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>
/// Candado por trámite: impide que dos migraciones del MISMO id de V1 se entrelacen.
/// <para>
/// Es un advisory lock de Postgres y no un <c>SemaphoreSlim</c> a propósito. El competidor típico
/// no es otra petición del mismo proceso: es la CONSOLA, corriendo en otro contenedor contra la
/// misma base mientras alguien dispara el endpoint. Un semáforo en memoria no la vería.
/// </para>
/// <para>
/// El lock es de SESIÓN, no de transacción: <c>ProcedureInstanceLoader</c> abre y confirma su
/// propia transacción, así que un <c>pg_try_advisory_xact_lock</c> se soltaría al terminar la
/// instancia 1 y dejaría las instancias 2 y 3 desprotegidas.
/// </para>
/// </summary>
public sealed class MigrationLock(FlitDbContext db)
{
    /// <summary>
    /// Intenta tomar el candado sin esperar. Devuelve <c>null</c> si otro lo tiene: quien llama
    /// debe rendirse (409), no encolarse — una migración en curso puede durar minutos.
    /// </summary>
    public async Task<MigrationLockHandle?> TryAcquireAsync(
        string v1Table, long v1Id, CancellationToken cancellationToken)
    {
        var key = MigrationLockKey.For(v1Table, v1Id);

        // La conexión se abre EXPLÍCITAMENTE y se mantiene abierta: un advisory lock de sesión
        // vive en la conexión, así que si EF la devolviera al pool entre transacciones el candado
        // se soltaría en silencio y este objeto sería una mentira. Por lo mismo el DbContext de
        // este proceso NO puede registrarse con EnableRetryOnFailure: esa estrategia puede
        // reabrir la conexión bajo nuestros pies.
        await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var taken = await db.Database
                .SqlQueryRaw<bool>(
                    "SELECT pg_try_advisory_lock({0}, {1}) AS \"Value\"",
                    key.Table, key.Tramite)
                .ToListAsync(cancellationToken);

            if (taken.Count > 0 && taken[0])
            {
                return new MigrationLockHandle(db, key);
            }
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }

        await db.Database.CloseConnectionAsync();
        return null;
    }
}

/// <summary>
/// Las dos claves de 32 bits del advisory lock.
/// <para>
/// Se calculan en el CLIENTE y no con el <c>hashtext()</c> de Postgres porque hay que pasar
/// exactamente los mismos dos enteros al tomar y al soltar, y <c>hashtext</c> no está garantizado
/// estable entre versiones mayores de Postgres: tras un upgrade, el <c>unlock</c> no soltaría el
/// mismo candado que se tomó y el trámite quedaría bloqueado hasta cerrar la conexión.
/// </para>
/// </summary>
internal readonly record struct MigrationLockKey(int Table, int Tramite)
{
    internal static MigrationLockKey For(string v1Table, long v1Id) =>
        // Los ids de V1 no llegan a 2^31 (el máximo real ronda 33.000), pero `checked` deja que
        // reviente ruidosamente si algún día lo hicieran, en vez de colisionar en silencio con
        // el candado de otro trámite.
        new(Fnv1a(v1Table), checked((int)v1Id));

    /// <summary>FNV-1a de 32 bits: determinista, estable y sin dependencias.</summary>
    private static int Fnv1a(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var c in value)
        {
            hash = (hash ^ c) * prime;
        }

        return unchecked((int)hash);
    }
}

/// <summary>Suelta el candado y devuelve la conexión al pool. Siempre con <c>await using</c>.</summary>
public sealed class MigrationLockHandle : IAsyncDisposable
{
    private readonly FlitDbContext db;
    private readonly MigrationLockKey key;

    internal MigrationLockHandle(FlitDbContext db, MigrationLockKey key)
    {
        this.db = db;
        this.key = key;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_unlock({0}, {1})", key.Table, key.Tramite);
        }
        finally
        {
            // Cerrar la conexión soltaría el candado igualmente; el unlock explícito está para que
            // no dependa de cuándo el pool decida reciclarla.
            await db.Database.CloseConnectionAsync();
        }
    }
}
