using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Ejecuta una operación bajo el contexto RLS de un tenant fijando
/// <c>app.current_tenant_id</c> con <c>set_config(..., is_local := true)</c> dentro de una
/// transacción — mismo patrón que <see cref="SignatureVaultRepository"/>/
/// <see cref="DbSignatureVaultReader"/>. En proveedor InMemory (tests) delega directo, sin
/// transacción ni set_config. Extraído para no duplicar el bloque en los repos/readers del
/// directorio de representantes legales (HU #10900).
///
/// <para>HU #13137 — si el contexto YA tiene una transacción abierta (p. ej. la baja de un mandatario, que
/// reasigna trámites evaluando el mandatario con lectores tenant-scoped dentro de su propia transacción) no se abre
/// otra —EF lanza «already in a transaction»—: se fija el tenant con <c>set_config(..., is_local := true)</c> durante
/// la operación y se restaura el valor anterior al terminar.</para>
/// </summary>
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

        if (context.Database.CurrentTransaction is not null)
        {
            var previous = await context.Database
                .SqlQuery<string>($"SELECT COALESCE(current_setting('app.current_tenant_id', true), '') AS \"Value\"")
                .SingleAsync(cancellationToken).ConfigureAwait(false);

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                cancellationToken).ConfigureAwait(false);
            try
            {
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {previous}, true)",
                    CancellationToken.None).ConfigureAwait(false);
            }
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
}
