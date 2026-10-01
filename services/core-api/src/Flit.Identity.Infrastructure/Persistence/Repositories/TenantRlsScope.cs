using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Ejecuta una operación bajo el contexto RLS de un tenant fijando
/// <c>app.current_tenant_id</c> con <c>set_config(..., is_local := true)</c> dentro de una
/// transacción — mismo patrón que <see cref="SignatureVaultRepository"/>/
/// <see cref="DbSignatureVaultReader"/>. En proveedor InMemory (tests) delega directo, sin
/// transacción ni set_config. Extraído para no duplicar el bloque en los repos/readers del
/// directorio de representantes legales (HU #10900). Recibe el <see cref="DatabaseFacade"/> y no un contexto concreto
/// (HU #13231): lo usan los repositorios de negocio de core-api y los de identidad, compartidos con core-identity.
/// </summary>
internal static class TenantRlsScope
{
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
}
