namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Repositorio de políticas de bloqueo de preflight por Organismo de Tránsito de un tenant
/// (FEATURE 05). La implementación (Infrastructure) aplica el contexto RLS del tenant
/// (<c>SET LOCAL app.current_tenant_id</c>) y persiste de forma atómica el estado deseado
/// junto con su auditoría en una sola transacción.
/// </summary>
public interface IOtBlockingPolicyRepository
{
    /// <summary>Lista todas las filas de política del tenant. Vacía si no hay ninguna.</summary>
    Task<IReadOnlyList<OtBlockingPolicyItem>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista las filas de política del tenant para un OT puntual (todos los criterios con override
    /// explícito). Vacía si no hay ninguna (se aplican los defaults por criterio) — query caliente
    /// del preflight, cubierta por el índice único de la tabla.
    /// </summary>
    Task<IReadOnlyList<OtBlockingPolicyItem>> ListForOfficeAsync(
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fija el estado deseado (<paramref name="blocks"/>) de <paramref name="criterion"/> para el
    /// par (tenant, OT). Idempotente: si la fila ya tiene ese mismo estado, no hace nada y no
    /// audita; si el estado cambia (o la fila no existía), persiste el upsert y registra una fila
    /// de auditoría en la misma transacción.
    /// </summary>
    Task SetAsync(
        Guid tenantId,
        Guid transitOfficeId,
        string criterion,
        bool blocks,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default);
}
