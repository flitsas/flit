using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistencia del agregado de prenda (IT-3, Feature #10585). Aislamiento por tenant en el <c>WHERE</c>
/// (mismo patrón que <see cref="ProcedureInstanceRepository"/>); la RLS de la tabla es defensa en profundidad.
/// </summary>
internal sealed class ProcedureInstancePrendaRepository(FlitDbContext db) : IProcedureInstancePrendaRepository
{
    public Task<ProcedureInstancePrenda?> GetVigenteAsync(Guid procedureInstanceId, Guid tenantId, CancellationToken ct = default) =>
        db.ProcedureInstancePrendas
            .FirstOrDefaultAsync(
                x => x.ProcedureInstanceId == procedureInstanceId
                    && x.TenantId == tenantId
                    && x.Estado == PrendaEstado.Vigente,
                ct);

    public async Task<IReadOnlyList<ProcedureInstancePrenda>> ListByInstanceAsync(Guid procedureInstanceId, Guid tenantId, CancellationToken ct = default) =>
        await db.ProcedureInstancePrendas
            .Where(x => x.ProcedureInstanceId == procedureInstanceId && x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(ProcedureInstancePrenda prenda, CancellationToken ct = default) =>
        await db.ProcedureInstancePrendas.AddAsync(prenda, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
