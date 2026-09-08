using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class VehicleSignatureImprintRepository(FlitDbContext db) : IVehicleSignatureImprintRepository
{
    public void Add(VehicleSignatureImprint row) => db.VehicleSignatureImprints.Add(row);

    public Task<VehicleSignatureImprint?> FindByDocumentHashAsync(
        string documentHash,
        CancellationToken cancellationToken = default) =>
        db.VehicleSignatureImprints.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DocumentHash == documentHash, cancellationToken);
}
