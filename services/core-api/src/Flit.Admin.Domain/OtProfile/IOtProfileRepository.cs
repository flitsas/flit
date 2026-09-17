namespace Flit.Admin.Domain.OtProfile;

public interface IOtProfileRepository
{
    Task<OtProfile?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee el perfil de una oficina por su <c>transit_office_id</c> (cross-tenant), para el
    /// SuperAdmin que navega el hub de una OT concreta. Solo lectura: no crea ni reasigna.
    /// </summary>
    Task<OtProfile?> GetByTransitOfficeAsync(Guid transitOfficeId, CancellationToken cancellationToken = default);

    /// <param name="revocationWindowBusinessDays">
    /// Ventana de revocatoria en días hábiles (HU #12568). Se persiste tal cual se recibe —
    /// incluido <c>null</c>, que significa "sin límite" (AC2) — sin sustituirlo por el valor
    /// previo ni por ningún default numérico.
    /// </param>
    Task<OtProfile> SaveAsync(
        Guid tenantId,
        string operationMode,
        bool quipuxReadOnly,
        Guid? changedBy,
        int? revocationWindowBusinessDays,
        Guid? transitOfficeId = null,
        CancellationToken cancellationToken = default);
}
