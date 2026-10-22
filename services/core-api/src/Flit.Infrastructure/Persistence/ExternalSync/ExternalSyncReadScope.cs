using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.ExternalSync;

/// <summary>
/// HU #13076 (Épica #12737, ADR-0066) — ámbito de lectura entre compañías del feed de sincronización
/// externa. Lo usa SOLO <see cref="Repositories.ProcedureSyncReadRepository"/>; una prueba de
/// arquitectura falla si otro tipo lo referencia (AC4).
///
/// <para>No es un permiso de RLS: el rol de core-api es propietario de las tablas de <c>tramites</c>,
/// que no declaran <c>FORCE ROW LEVEL SECURITY</c>, así que las políticas ya no se le aplican (ver
/// <c>QuipuxSubmissionRepository</c>). Lo que da este ámbito es:</para>
/// <list type="bullet">
///   <item>una transacción <c>REPEATABLE READ READ ONLY</c>: toda la página sale de una misma
///   instantánea, y el límite <c>pg_snapshot_xmin</c> que filtra la consulta es el de esa instantánea;</item>
///   <item><c>SET LOCAL row_security = off</c> como aserción: si algún día el rol dejara de saltarse RLS,
///   la consulta FALLA en vez de devolver en silencio un feed vacío (sin GUC de tenant la política no
///   deja ver ninguna fila).</item>
/// </list>
/// </summary>
internal sealed class ExternalSyncReadScope(FlitDbContext context)
{
    public async Task<T> ExecuteAsync<T>(
        Func<DbConnection, DbTransaction, Task<T>> body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await context.Database
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlRawAsync(
                    "SET TRANSACTION READ ONLY", cancellationToken).ConfigureAwait(false);
                await context.Database.ExecuteSqlRawAsync(
                    "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);

                var result = await body(context.Database.GetDbConnection(), transaction.GetDbTransaction())
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}
