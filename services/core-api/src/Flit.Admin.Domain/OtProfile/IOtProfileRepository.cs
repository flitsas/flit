namespace Flit.Admin.Domain.OtProfile;

public interface IOtProfileRepository
{
    Task<OtProfile?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee el perfil de una oficina por su <c>transit_office_id</c> (cross-tenant), para el
    /// SuperAdmin que navega el hub de una OT concreta. Solo lectura: no crea ni reasigna.
    /// </summary>
    Task<OtProfile?> GetByTransitOfficeAsync(Guid transitOfficeId, CancellationToken cancellationToken = default);

    Task<OtProfile> SaveAsync(
        Guid tenantId,
        string operationMode,
        bool quipuxReadOnly,
        Guid? changedBy,
        Guid? transitOfficeId = null,
        CancellationToken cancellationToken = default);
}
