namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Repositorio de los grants de organismos de tránsito por tenant (HU #10192, RF13).
/// La implementación (Infrastructure) aplica el contexto RLS de SuperAdmin
/// (<c>SET LOCAL app.current_tenant_id</c>) y persiste de forma atómica el grant
/// junto con su auditoría en una sola transacción.
/// </summary>
public interface ITransitGrantRepository
{
    /// <summary>
    /// Habilita el organismo para el tenant (AC2). Idempotente: si el grant ya existe
    /// devuelve <c>false</c> sin duplicar fila ni auditoría; si lo crea devuelve
    /// <c>true</c> y registra una fila de auditoría.
    /// </summary>
    Task<bool> AddGrantAsync(
        Guid tenantId,
        Guid transitOfficeId,
        Guid? createdBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deshabilita (elimina) el grant del tenant (AC3). Devuelve <c>false</c> si no
    /// existía (→ 404); si lo elimina devuelve <c>true</c> y registra auditoría.
    /// </summary>
    Task<bool> RemoveGrantAsync(
        Guid tenantId,
        Guid transitOfficeId,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Lista los ids de organismos habilitados del tenant (AC5).</summary>
    Task<IReadOnlyList<Guid>> ListEnabledOfficeIdsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
