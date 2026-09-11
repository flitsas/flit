using Flit.Tramites.Domain.Entities.BulkTramites;

namespace Flit.Tramites.Domain.Repositories;

public interface IBulkTramitesBatchRepository
{
    Task AddAsync(BulkTramitesBatch batch, CancellationToken ct = default);

    Task<BulkTramitesBatch?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lotes del tenant, más recientes primero (HU #12524 — resumen en /tramites).</summary>
    Task<List<BulkTramitesBatch>> ListByTenantAsync(Guid tenantId, int top, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
