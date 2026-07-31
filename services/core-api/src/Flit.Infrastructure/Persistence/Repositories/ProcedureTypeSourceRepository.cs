using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de las fuentes externas por tipo de trámite (FEATURE-08 / CFD-04). Catálogo global
/// sin <c>tenant_id</c> (excepción ADR-0019). El código de la fuente se resuelve por join contra
/// <c>tramites.external_data_sources</c> en las lecturas.
/// </summary>
internal sealed class ProcedureTypeSourceRepository(FlitDbContext db) : IProcedureTypeSourceRepository
{
    public async Task<IReadOnlyList<ProcedureTypeSourceRecord>> ListByTypeAsync(
        Guid procedureTypeId, CancellationToken ct)
    {
        return await db.ProcedureTypeSources
            .AsNoTracking()
            .Where(s => s.ProcedureTypeId == procedureTypeId)
            .OrderBy(s => s.ExecutionOrder)
            .Join(
                db.ExternalDataSources.AsNoTracking(),
                s => s.ExternalDataSourceId,
                eds => eds.Id,
                (s, eds) => new ProcedureTypeSourceRecord(
                    s.ExternalDataSourceId, eds.Code, s.ExecutionOrder, s.Config))
            .ToListAsync(ct);
    }

    public async Task ReplaceSourcesAsync(
        Guid procedureTypeId, IReadOnlyList<ProcedureTypeSourceUpsert> sources, CancellationToken ct)
    {
        var existing = await db.ProcedureTypeSources
            .Where(s => s.ProcedureTypeId == procedureTypeId)
            .ToListAsync(ct);
        db.ProcedureTypeSources.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        foreach (var s in sources)
        {
            db.ProcedureTypeSources.Add(new ProcedureTypeSource
            {
                ProcedureTypeId = procedureTypeId,
                ExternalDataSourceId = s.ExternalDataSourceId,
                IsActive = true,
                ExecutionOrder = (short)s.ExecutionOrder,
                Config = string.IsNullOrWhiteSpace(s.Config) ? "{}" : s.Config,
                CreatedAt = now,
            });
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct).ContinueWith(_ => { }, ct);
}
