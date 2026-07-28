using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Persistencia de la caché cross-trámite de consultas externas (HU #10878, ADR-0030). Aislamiento
/// por tenant en el <c>WHERE</c> (mismo patrón que <see cref="IProcedureInstanceRepository"/>); la
/// RLS de la tabla es defensa en profundidad. Las entradas devueltas por <c>Find*Async</c> quedan
/// TRACKEADAS por el contexto: el servicio de aplicación puede mutarlas directamente (p. ej.
/// <c>ReuseCount</c>/<c>LastReusedAt</c> en un hit, o el payload/expiración en un upsert) y
/// persistir con <see cref="SaveChangesAsync"/>, sin un método <c>Update</c> explícito.
/// </summary>
public interface IExternalQueryCacheRepository
{
    /// <summary>Entrada vigente-o-no de persona por <c>(tenant, fuente, tipoDoc, documento)</c>; null si nunca se cacheó.</summary>
    Task<ExternalQueryCacheEntry?> FindPersonAsync(
        Guid tenantId, Guid externalDataSourceId, string documentType, string documentNumber,
        CancellationToken ct = default);

    /// <summary>Entrada vigente-o-no de vehículo por <c>(tenant, fuente, identificador)</c>; null si nunca se cacheó.</summary>
    Task<ExternalQueryCacheEntry?> FindVehicleAsync(
        Guid tenantId, Guid externalDataSourceId, string vehicleIdentifier,
        CancellationToken ct = default);

    Task AddAsync(ExternalQueryCacheEntry entry, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
