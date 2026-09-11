using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class BulkTramitesBatchRepository(FlitDbContext db) : IBulkTramitesBatchRepository
{
    public async Task AddAsync(BulkTramitesBatch batch, CancellationToken ct = default) =>
        await db.BulkTramitesBatches.AddAsync(batch, ct).ConfigureAwait(false);

    public Task<BulkTramitesBatch?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.BulkTramitesBatches
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<List<BulkTramitesBatch>> ListByTenantAsync(Guid tenantId, int top, CancellationToken ct = default) =>
        db.BulkTramitesBatches
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(top)
            .ToListAsync(ct);

    public Task<List<Guid>> ListQueuedIdsAsync(int top, CancellationToken ct = default) =>
        db.BulkTramitesBatches
            .AsNoTracking()
            .Where(b => b.Status == BulkTramitesBatchStatus.Queued)
            .OrderBy(b => b.CreatedAt)
            .Take(top)
            .Select(b => b.Id)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
