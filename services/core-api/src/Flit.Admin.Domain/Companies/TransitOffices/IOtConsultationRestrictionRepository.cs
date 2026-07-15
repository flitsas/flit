namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Repositorio de restricciones de consulta por Organismo de Tránsito de un tenant
/// (HU #10759). La implementación (Infrastructure) aplica el contexto RLS del tenant
/// (<c>SET LOCAL app.current_tenant_id</c>) y persiste de forma atómica el estado deseado
/// junto con su auditoría en una sola transacción.
/// </summary>
public interface IOtConsultationRestrictionRepository
{
    /// <summary>Lista todas las filas de restricción del tenant (AC1/AC5). Vacía si no hay ninguna.</summary>
    Task<IReadOnlyList<OtConsultationRestrictionItem>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista los <c>consultation_kind</c> inhabilitados (<c>enabled = false</c>) del tenant
    /// para un OT puntual. Vacía si no hay ninguna fila (permisivo por defecto) — query
    /// caliente del preflight, cubierta por el índice parcial de la tabla.
    /// </summary>
    Task<IReadOnlyList<string>> ListDisabledKindsAsync(
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fija el estado deseado (<paramref name="enabled"/>) de <paramref name="consultationKind"/>
    /// para el par (tenant, OT). Idempotente: si la fila ya tiene ese mismo estado, no hace
    /// nada y no audita; si el estado cambia (o la fila no existía), persiste el upsert y
    /// registra una fila de auditoría en la misma transacción.
    /// </summary>
    Task SetAsync(
        Guid tenantId,
        Guid transitOfficeId,
        string consultationKind,
        bool enabled,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default);
}
