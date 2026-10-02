using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Ejecuta una operación bajo el contexto RLS de un tenant fijando
/// <c>app.current_tenant_id</c> con <c>set_config(..., is_local := true)</c> dentro de una
/// transacción — mismo patrón que <see cref="SignatureVaultRepository"/>/
/// <see cref="DbSignatureVaultReader"/>. En proveedor InMemory (tests) delega directo, sin
/// transacción ni set_config. Extraído para no duplicar el bloque en los repos/readers del
/// directorio de representantes legales (HU #10900).
/// </summary>
/// <remarks>
/// <para><b>Bug #13194 (P4) — transacción ambiente.</b> Si el contexto YA tiene una transacción abierta
/// (p. ej. el procesador del outbox de validaciones de identidad, que reclama la fila con
/// <c>FOR UPDATE</c> y llama a los consumidores dentro de su transacción), abrir otra lanzaba
/// <c>InvalidOperationException</c> ("The connection is already in a transaction…"): el evento agotaba
/// sus reintentos y la firma de los pendientes de la persona no se propagaba nunca. Ahora se UNE a la
/// transacción ambiente con un SAVEPOINT:</para>
/// <list type="bullet">
///   <item>fija el tenant con <c>set_config(..., true)</c> y, al terminar bien, RESTAURA el valor previo
///   del ámbito (lo que venga después en la misma transacción —otro tenant u otro evento— no hereda el
///   contexto RLS) y libera el savepoint;</item>
///   <item>si la operación falla, revierte al savepoint —deshace sus escrituras y también el
///   <c>set_config</c>, que es transaccional— y relanza, dejando la transacción ambiente utilizable para
///   que su dueño decida (el outbox sella el intento y confirma).</item>
/// </list>
/// <para>No confirma ni revierte la transacción ambiente: es de quien la abrió. Sin transacción ambiente
/// el comportamiento es el de siempre (transacción propia dentro de la execution strategy).</para>
/// </remarks>
internal static class TenantRlsScope
{
    public static async Task<T> ExecuteAsync<T>(
        FlitDbContext context,
        Guid tenantId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
        {
            return await operation().ConfigureAwait(false);
        }

        if (context.Database.CurrentTransaction is { } ambiente)
        {
            return await ExecuteInAmbientTransactionAsync(context, ambiente, tenantId, operation, cancellationToken)
                .ConfigureAwait(false);
        }

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                    cancellationToken).ConfigureAwait(false);

                var result = await operation().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private static async Task<T> ExecuteInAmbientTransactionAsync<T>(
        FlitDbContext context,
        IDbContextTransaction ambiente,
        Guid tenantId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        // Nombre único por llamada: tolera ámbitos anidados sin pisarse.
        var savepoint = "tenant_rls_" + Guid.NewGuid().ToString("N");

        // Valor del ámbito actual ('' si nunca se fijó en la sesión) para restaurarlo al salir.
        var previo = await context.Database
            .SqlQuery<string>($"SELECT coalesce(current_setting('app.current_tenant_id', true), '') AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        await ambiente.CreateSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
        T result;
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                cancellationToken).ConfigureAwait(false);

            result = await operation().ConfigureAwait(false);
        }
        catch
        {
            // Deshace las escrituras de la operación y el set_config; la transacción ambiente sigue viva.
            await ambiente.RollbackToSavepointAsync(savepoint, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {previo}, true)",
            cancellationToken).ConfigureAwait(false);
        await ambiente.ReleaseSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
