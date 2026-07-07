namespace Flit.Admin.Domain.OtRequirements;

/// <summary>
/// Persistencia de los requisitos por OT (HU #10545). RLS por <c>tenant_id</c> del organismo;
/// el <c>transit_office_id</c> es único por fila.
/// </summary>
public interface IOtRequirementsRepository
{
    /// <summary>Requisitos del OT del tenant, o <c>null</c> si aún no hay fila configurada.</summary>
    Task<OtRequirements?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Upsert de los requisitos del OT del tenant. Devuelve el estado persistido.</summary>
    Task<OtRequirements> SaveAsync(
        Guid tenantId,
        bool requiresRnmc,
        bool allowPlatePreassign,
        bool identityValidationEnabled,
        Guid? changedBy,
        Guid? transitOfficeId = null,
        CancellationToken cancellationToken = default);
}
