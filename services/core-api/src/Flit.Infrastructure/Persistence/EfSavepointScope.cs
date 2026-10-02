using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Implementación EF de <see cref="ISavepointScope"/> (Bug #13194, review PR #510 MAYOR-2): savepoint en la
/// transacción ambiente; ante error revierte a él, desacopla las entidades que la operación dejó rastreadas
/// y relanza la excepción original (o <see cref="AggregateException"/> si además falla el rollback).
/// </summary>
internal sealed class EfSavepointScope(FlitDbContext context) : ISavepointScope
{
    public async Task<T> EjecutarAsync<T>(Func<Task<T>> operacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);

        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not { } ambiente)
            return await operacion().ConfigureAwait(false);

        var savepoint = SavepointEf.NuevoNombre("unidad");
        var previas = SavepointEf.Rastreadas(context);
        await ambiente.CreateSavepointAsync(savepoint, ct).ConfigureAwait(false);

        T resultado;
        try
        {
            resultado = await operacion().ConfigureAwait(false);
        }
        catch (Exception original)
        {
            await SavepointEf.RevertirAsync(context, ambiente, savepoint, previas, original).ConfigureAwait(false);
            throw;
        }

        await ambiente.ReleaseSavepointAsync(savepoint, CancellationToken.None).ConfigureAwait(false);
        return resultado;
    }
}
