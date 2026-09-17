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

    /// <summary>
    /// Lotes del tenant CON sus filas: el resumen de HU #12524 cuenta creados/por retomar/no
    /// creados a partir de ellas. Sin el Include los contadores salían todos en cero sobre lotes
    /// que sí tenían resultados — y no lo delataba ningún test, porque los dobles de prueba y EF
    /// InMemory entregan la navegación poblada igual. Se vio consultando el endpoint de verdad.
    /// </summary>
    public Task<List<BulkTramitesBatch>> ListByTenantAsync(Guid tenantId, int top, CancellationToken ct = default) =>
        db.BulkTramitesBatches
            .AsNoTracking()
            .Include(b => b.Rows)
            .Where(b => b.TenantId == tenantId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(top)
            .ToListAsync(ct);

    public Task<List<Guid>> ListQueuedIdsAsync(int top, DateTimeOffset reclaimBefore, CancellationToken ct = default) =>
        db.BulkTramitesBatches
            .AsNoTracking()
            .Where(b => b.Status == BulkTramitesBatchStatus.Queued
                // Sin avance = ninguna fila resuelta desde reclaimBefore y el lote nació antes.
                || (b.Status == BulkTramitesBatchStatus.Processing
                    && b.CreatedAt < reclaimBefore
                    && !b.Rows.Any(r => r.ProcessedAt != null && r.ProcessedAt >= reclaimBefore)))
            .OrderBy(b => b.CreatedAt)
            .Take(top)
            .Select(b => b.Id)
            .ToListAsync(ct);

    /// <summary>
    /// El procesador del lote comparte el DbContext con los casos de uso del wizard que invoca, y
    /// alguno puede dejar una entidad ajena sucia con un token de concurrencia viejo (p. ej. la
    /// caché de consultas, cuyo <c>row_version</c> sube un trigger). Si eso ocurre, la fila del lote
    /// no puede quedarse sin guardar —el lote entero se quedaría «en proceso» para siempre—: las
    /// entidades ajenas en conflicto se sueltan y se guarda lo propio.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.All(e =>
            e.Entity is not BulkTramitesBatch and not BulkTramitesBatchRow))
        {
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
