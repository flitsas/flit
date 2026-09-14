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

    /// <summary>
    /// La caché es «último en escribir gana»: el <c>row_version</c> lo sube un trigger en la base y
    /// EF no refresca el valor rastreado tras guardar, así que la SEGUNDA escritura a la misma fila
    /// dentro del mismo DbContext choca por token viejo. El wizard nunca la escribe dos veces en una
    /// petición; la carga masiva sí (reintento de la consulta de persona, HU #12523), y sin esto el
    /// reintento no podía acertar nunca. Ante el choque se toma el token actual de la base y se
    /// reintenta una vez.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.All(e => e.Entity is ExternalQueryCacheEntry))
        {
            foreach (var entry in ex.Entries)
            {
                var actual = await entry.GetDatabaseValuesAsync(ct).ConfigureAwait(false);
                if (actual is null)
                {
                    entry.State = EntityState.Detached;
                    continue;
                }

                entry.Property(nameof(ExternalQueryCacheEntry.RowVersion)).OriginalValue =
                    actual[nameof(ExternalQueryCacheEntry.RowVersion)];
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
