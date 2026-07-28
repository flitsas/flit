using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistencia de la caché cross-trámite de consultas externas (HU #10878, ADR-0030). Aislamiento
/// por tenant en el <c>WHERE</c> (mismo patrón que <see cref="ProcedureInstanceRepository"/>); la
/// RLS de la tabla es defensa en profundidad.
/// </summary>
internal sealed class ExternalQueryCacheRepository(FlitDbContext db) : IExternalQueryCacheRepository
{
    public Task<ExternalQueryCacheEntry?> FindPersonAsync(
        Guid tenantId, Guid externalDataSourceId, string documentType, string documentNumber,
        CancellationToken ct = default) =>
        db.ExternalQueryCache.FirstOrDefaultAsync(
            x => x.TenantId == tenantId
                && x.ExternalDataSourceId == externalDataSourceId
                && x.SubjectKind == ExternalQueryCacheRules.SubjectKindPerson
                && x.DocumentType == documentType
                && x.DocumentNumber == documentNumber,
            ct);

    public Task<ExternalQueryCacheEntry?> FindVehicleAsync(
        Guid tenantId, Guid externalDataSourceId, string vehicleIdentifier,
        CancellationToken ct = default) =>
        db.ExternalQueryCache.FirstOrDefaultAsync(
            x => x.TenantId == tenantId
                && x.ExternalDataSourceId == externalDataSourceId
                && x.SubjectKind == ExternalQueryCacheRules.SubjectKindVehicle
                && x.VehicleIdentifier == vehicleIdentifier,
            ct);

    public async Task AddAsync(ExternalQueryCacheEntry entry, CancellationToken ct = default) =>
        await db.ExternalQueryCache.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
