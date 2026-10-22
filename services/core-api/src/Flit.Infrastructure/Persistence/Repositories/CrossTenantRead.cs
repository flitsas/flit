using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Ejecuta una lectura o escritura cross-tenant con <c>SET LOCAL row_security = off</c> dentro de una transacción
/// (propia, o la ya abierta). Misma técnica que <c>DbMandateSignerReader</c> y <c>MandateSignerDirectory</c>;
/// con proveedor no relacional (InMemory en pruebas) solo ejecuta la acción. Lo usan las rutas de mandatarios
/// que cruzan compañías (HU #13176, #13178) y que deben aplicar su propio alcance por perfil antes de llamar.
/// </summary>
internal static class CrossTenantRead
{
    public static async Task<T> ExecuteAsync<T>(
        FlitDbContext context,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        if (!context.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        if (context.Database.CurrentTransaction is not null)
        {
            await context.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                .ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                    .ConfigureAwait(false);
                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}
