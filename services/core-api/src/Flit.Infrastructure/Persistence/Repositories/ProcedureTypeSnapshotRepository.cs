using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio del snapshot inmutable del tipo de trámite (FEATURE-08 / CFD-01 / AC#5).
/// Mapea el record de dominio <see cref="ProcedureTypeSnapshotRecord"/> ↔ la entidad de persistencia
/// <see cref="ProcedureTypeSnapshot"/> (tabla <c>tramites.procedure_type_snapshots</c>, RLS por tenant).
/// </summary>
internal sealed class ProcedureTypeSnapshotRepository(FlitDbContext db) : IProcedureTypeSnapshotRepository
{
    public async Task AddAsync(ProcedureTypeSnapshotRecord snapshot, CancellationToken ct)
    {
        var entity = new ProcedureTypeSnapshot
        {
            Id = snapshot.Id,
            TenantId = snapshot.TenantId,
            ProcedureInstanceId = snapshot.ProcedureInstanceId,
            ProcedureTypeId = snapshot.ProcedureTypeId,
            TypeVersion = snapshot.TypeVersion,
            Snapshot = snapshot.Snapshot,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = snapshot.CreatedBy,
        };

        await db.ProcedureTypeSnapshots.AddAsync(entity, ct);
    }

    public async Task<ProcedureTypeSnapshotRecord?> GetByInstanceIdAsync(
        Guid procedureInstanceId, Guid tenantId, CancellationToken ct)
    {
        var e = await db.ProcedureTypeSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ProcedureInstanceId == procedureInstanceId && x.TenantId == tenantId, ct);

        return e is null
            ? null
            : new ProcedureTypeSnapshotRecord(
                e.Id, e.TenantId, e.ProcedureInstanceId, e.ProcedureTypeId, e.TypeVersion, e.Snapshot, e.CreatedBy);
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct).ContinueWith(_ => { }, ct);
}
