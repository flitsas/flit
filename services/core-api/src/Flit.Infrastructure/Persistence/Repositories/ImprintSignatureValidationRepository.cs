using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class ImprintSignatureValidationRepository(FlitDbContext db) : IImprintSignatureValidationRepository
{
    public void Add(ImprintSignatureValidation row) => db.ImprintSignatureValidations.Add(row);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ImprintSignatureValidation>> GetLatestByImprintIdsAsync(
        IReadOnlyCollection<Guid> imprintIds,
        CancellationToken cancellationToken = default)
    {
        if (imprintIds.Count == 0)
            return new Dictionary<Guid, ImprintSignatureValidation>();

        var ids = imprintIds.Distinct().ToArray();
        var rows = await db.ImprintSignatureValidations
            .AsNoTracking()
            .Where(v => ids.Contains(v.VehicleSignatureImprintId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(v => v.VehicleSignatureImprintId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(v => v.ValidatedAt).ThenByDescending(v => v.Id).First());
    }

    public async Task<IReadOnlyList<ImprintSignatureValidation>> ListByImprintIdAsync(
        Guid vehicleSignatureImprintId,
        CancellationToken cancellationToken = default)
    {
        return await db.ImprintSignatureValidations
            .AsNoTracking()
            .Where(v => v.VehicleSignatureImprintId == vehicleSignatureImprintId)
            .OrderByDescending(v => v.ValidatedAt)
            .ThenByDescending(v => v.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
