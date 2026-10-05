using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Ejecuta una operación bajo el contexto RLS de un tenant fijando
/// <c>app.current_tenant_id</c> con <c>set_config(..., is_local := true)</c> dentro de una
/// transacción — mismo patrón que <see cref="SignatureVaultRepository"/>/
/// <see cref="DbSignatureVaultReader"/>. En proveedor InMemory (tests) delega directo, sin
/// transacción ni set_config. Extraído para no duplicar el bloque en los repos/readers del
/// directorio de representantes legales (HU #10900). Acepta el contexto o su <see cref="DatabaseFacade"/> (HU #13231):
/// lo usan los repositorios de negocio de core-api y los de identidad, compartidos con core-identity.
///
/// <para>HU #13137 — si el contexto YA tiene una transacción abierta (p. ej. la baja de un mandatario, que
/// reasigna trámites evaluando el mandatario con lectores tenant-scoped dentro de su propia transacción) no se abre
/// otra —EF lanza «already in a transaction»—: se fija el tenant con <c>set_config(..., is_local := true)</c> durante
/// la operación y se restaura el valor anterior al terminar.</para>
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
/// <para>Review PR #510 (L3/MENOR-2): la restauración del tenant y el RELEASE van con
/// <see cref="CancellationToken.None"/>; si la restauración falla también se revierte al savepoint. Si el
/// propio rollback falla, se lanza <see cref="AggregateException"/> con la excepción ORIGINAL primero. Tras
/// revertir, las entidades que la operación dejó rastreadas (insertadas o cargadas) se desacoplan del change
/// tracker: sus filas ya no existen y un SaveChanges posterior del dueño de la transacción no debe
/// arrastrarlas.</para>
/// <para>No confirma ni revierte la transacción ambiente: es de quien la abrió. Sin transacción ambiente
/// el comportamiento es el de siempre (transacción propia dentro de la execution strategy).</para>
/// </remarks>
internal static class TenantRlsScope
{
    public static Task<T> ExecuteAsync<T>(
        DbContext context,
        Guid tenantId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context.Database, tenantId, operation, cancellationToken);

    public static async Task<T> ExecuteAsync<T>(
        DatabaseFacade database,
        Guid tenantId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (!database.IsRelational())
        {
            return await operation().ConfigureAwait(false);
        }

        // HU #13137 y Bug #13194: si ya hay transacción, no se abre otra. El savepoint de DEV restaura
        // el tenant anterior (lo que pedía la baja del mandatario) y deja viva la transacción ambiente.
        if (database.CurrentTransaction is { } ambiente)
        {
            var context = database.GetService<ICurrentDbContext>().Context;
            return await ExecuteInAmbientTransactionAsync(context, ambiente, tenantId, operation, cancellationToken)
                .ConfigureAwait(false);
        }

        var strategy = database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                    cancellationToken).ConfigureAwait(false);

                var result = await operation().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private static async Task<T> ExecuteInAmbientTransactionAsync<T>(
        DbContext context,
        IDbContextTransaction ambiente,
        Guid tenantId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        // Nombre único por llamada: tolera ámbitos anidados sin pisarse.
        var savepoint = SavepointEf.NuevoNombre("tenant_rls");

        // Valor del ámbito actual ('' si nunca se fijó en la sesión) para restaurarlo al salir.
        var previo = await context.Database
            .SqlQuery<string>($"SELECT coalesce(current_setting('app.current_tenant_id', true), '') AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        var previas = SavepointEf.Rastreadas(context);
        await ambiente.CreateSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
        T result;
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                cancellationToken).ConfigureAwait(false);

            result = await operation().ConfigureAwait(false);
        }
        catch (Exception original)
        {
            // Deshace las escrituras de la operación y el set_config; la transacción ambiente sigue viva.
            await SavepointEf.RevertirAsync(context, ambiente, savepoint, previas, original).ConfigureAwait(false);
            throw;
        }

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {previo}, true)",
                CancellationToken.None).ConfigureAwait(false);
            await ambiente.ReleaseSavepointAsync(savepoint, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception restauracion)
        {
            // Sin restaurar el tenant no se puede dejar el ámbito: se revierte todo lo de la operación.
            await SavepointEf.RevertirAsync(context, ambiente, savepoint, previas, restauracion).ConfigureAwait(false);
            throw;
        }

        return result;
    }
}
