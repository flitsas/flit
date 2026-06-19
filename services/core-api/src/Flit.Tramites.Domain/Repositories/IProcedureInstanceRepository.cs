using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

public interface IProcedureInstanceRepository
{
    Task<ProcedureInstance?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<ProcedureInstance?> GetByIdWithDetailsAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<int> CountByTenantAndYearAsync(Guid tenantId, int year, CancellationToken ct = default);
    Task AddAsync(ProcedureInstance instance, CancellationToken ct = default);
    Task UpdateAsync(ProcedureInstance instance, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
