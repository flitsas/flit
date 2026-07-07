namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>Desenlace del cambio de estado de un tenant OT (HU #10518).</summary>
public enum SetTransitOfficeTenantStatusOutcome
{
    /// <summary>Estado aplicado (HTTP 200). Incluye el cambio efectivo o el no-op idempotente.</summary>
    Updated,

    /// <summary>El tenant OT no existe (HTTP 404).</summary>
    NotFound,
}

/// <summary>
/// Resultado de activar/desactivar un tenant OT. <see cref="Changed"/> distingue el cambio
/// real (que dejó auditoría) del no-op idempotente (mismo estado, sin auditoría duplicada).
/// </summary>
public sealed record SetTransitOfficeTenantStatusResult(
    SetTransitOfficeTenantStatusOutcome Outcome,
    Guid TenantId,
    bool EstadoActivo,
    bool Changed)
{
    public static SetTransitOfficeTenantStatusResult NotFound(Guid tenantId) =>
        new(SetTransitOfficeTenantStatusOutcome.NotFound, tenantId, false, false);

    public static SetTransitOfficeTenantStatusResult Applied(Guid tenantId, bool estadoActivo, bool changed) =>
        new(SetTransitOfficeTenantStatusOutcome.Updated, tenantId, estadoActivo, changed);
}
