namespace Flit.Admin.Domain.RejectionReasons;

/// <summary>
/// Catálogo global de causales de rechazo. Sin tenant: lo administra SuperAdmin y lo consumen
/// todos los organismos, que es lo que mantiene comparable el reporte de motivos entre organismos
/// y entre empresas.
/// </summary>
public interface IRejectionReasonRepository
{
    /// <summary>
    /// Lista el catálogo ordenado por familia y orden de presentación.
    /// <paramref name="familia"/> nula devuelve todas las modalidades.
    /// </summary>
    Task<IReadOnlyList<RejectionReasonItem>> ListAsync(
        string? familia,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<RejectionReasonItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>¿Existe otra causal con este código? (unicidad global; excluye <paramref name="excludeId"/>).</summary>
    Task<bool> CodeExistsAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    Task<RejectionReasonItem> CreateAsync(
        string code,
        string description,
        string familia,
        int sortOrder,
        Guid? createdBy,
        CancellationToken cancellationToken = default);

    Task<RejectionReasonItem?> UpdateAsync(
        Guid id,
        string code,
        string description,
        string familia,
        int sortOrder,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa o desactiva la causal. No hay borrado: una causal retirada debe seguir resolviendo el
    /// nombre de los rechazos históricos que la usaron.
    /// </summary>
    Task<RejectionReasonItem?> SetActiveAsync(
        Guid id,
        bool isActive,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve qué ids del catálogo son válidos y están activos para la familia dada. Lo usa el
    /// rechazo del OT para no persistir causales inventadas o de otra familia.
    /// </summary>
    Task<IReadOnlyList<Guid>> FilterValidIdsAsync(
        IReadOnlyList<Guid> candidateIds,
        string familia,
        CancellationToken cancellationToken = default);
}
