namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Repositorio de la configuración operativa del tenant (HU #10190).
/// La implementación (Infrastructure) aplica el contexto RLS de SuperAdmin
/// (<c>SET LOCAL app.current_tenant_id</c>) y persiste de forma atómica la
/// política y su auditoría en una sola transacción.
/// </summary>
public interface ITenantSettingsRepository
{
    /// <summary>Lee la configuración actual del tenant, o <c>null</c> si no existe.</summary>
    Task<TenantSettings?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert atómico de <paramref name="settings"/> + inserción de una fila de
    /// auditoría por cada elemento de <paramref name="changes"/>, todo en una
    /// única transacción (todo o nada).
    /// </summary>
    Task SaveAsync(
        TenantSettings settings,
        IReadOnlyList<TenantConfigChange> changes,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default);
}
