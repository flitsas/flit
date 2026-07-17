using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>
/// Lee valores de referencia de avalúo de <c>tramites.avaluo_mock_values</c> por VIN/placa y fuente.
/// Habilita el modo mock de los proveedores en DEV/QA (tabla sembrada por la migración del Feature #10707).
/// En producción la tabla arranca vacía → devuelve null.
/// </summary>
internal sealed class AvaluoMockValueReader(FlitDbContext db)
{
    public async Task<long?> GetValueAsync(string matchKey, string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(matchKey))
            return null;

        var key = matchKey.Trim().ToUpperInvariant();
        var row = await db.AvaluoMockValues
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.MatchKey == key && x.Source == source, ct);

        return row is null ? null : (long)decimal.Round(row.ValueCop, MidpointRounding.AwayFromZero);
    }
}
