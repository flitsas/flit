using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Lee DIVIPO y banderas Quipux de <c>catalogs.transit_offices</c> para el submit (Bug #13912).
/// </summary>
internal sealed class TransitOfficeQuipuxCanalReader(FlitDbContext db) : ITransitOfficeQuipuxCanalReader
{
    public async Task<TransitOfficeQuipuxCanal?> GetAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.TransitOffices
            .AsNoTracking()
            .Where(o => o.Id == transitOfficeId)
            .Select(o => new TransitOfficeQuipuxCanal(
                o.DivipoCode,
                o.QuipuxRegistration,
                o.QuipuxTransfer,
                o.QuipuxOther))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row;
    }
}
