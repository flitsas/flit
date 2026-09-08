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
            .Where(x => x.DeletedAt == null && x.DocumentHash == documentHash)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> ListSignedAttachmentIdsForInstanceAsync(
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default)
    {
        var ids = await db.VehicleSignatureImprints.AsNoTracking()
            .Where(x => x.ProcedureInstanceId == procedureInstanceId && x.AttachmentId != null)
            .Select(x => x.AttachmentId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ids.Count == 0 ? new HashSet<Guid>() : ids.ToHashSet();
    }

    public IReadOnlySet<string> SoftDeleteByAttachmentIds(
        IEnumerable<Guid> attachmentIds,
        DateTimeOffset deletedAt)
    {
        var ids = attachmentIds as ICollection<Guid> ?? attachmentIds.ToList();
        if (ids.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        // Snapshot paths ANTES del update (AsNoTracking: no pelear con SaveChanges del replace).
        var preserve = db.VehicleSignatureImprints
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.AttachmentId != null && ids.Contains(x.AttachmentId.Value))
            .Select(x => x.SignedStoragePath)
            .AsEnumerable()
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        // ExecuteUpdate inmediato: evita DbUpdateConcurrencyException cuando el mismo SaveChanges
        // borra el adjunto y la FK ON DELETE SET NULL + trg_row_version invalidan el token tracked.
        db.VehicleSignatureImprints
            .IgnoreQueryFilters()
            .Where(x => x.AttachmentId != null && ids.Contains(x.AttachmentId.Value))
            .ExecuteUpdate(setters => setters
                .SetProperty(x => x.DeletedAt, deletedAt)
                .SetProperty(x => x.AttachmentId, (Guid?)null)
                .SetProperty(x => x.UpdatedAt, deletedAt));

        return preserve;
    }
}
