using Flit.Tramites.Domain.Entities.BulkTramites;

namespace Flit.Tramites.Domain.Repositories;

public interface IBulkTramitesBatchRepository
{
    Task AddAsync(BulkTramitesBatch batch, CancellationToken ct = default);

    Task<BulkTramitesBatch?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lotes del tenant, más recientes primero (HU #12524 — resumen en /tramites).</summary>
    Task<List<BulkTramitesBatch>> ListByTenantAsync(Guid tenantId, int top, CancellationToken ct = default);

    /// <summary>
    /// Ids de los lotes en cola, más antiguos primero (HU #12523 — los reclama el worker). Devuelve
    /// solo ids: el lote con sus filas lo carga el procesador cuando le toca.
    /// </summary>
    Task<List<Guid>> ListQueuedIdsAsync(int top, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
