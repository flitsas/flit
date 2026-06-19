namespace Flit.Admin.Domain.DocumentOrderOverrides;

/// <summary>
/// Repositorio de overrides de orden documental por OT/Cliente (HU #10196, RF09–RF16).
/// La implementación (Infrastructure) opera sobre <c>tramites.document_order_overrides</c>
/// con queries parametrizadas EF LINQ. El DELETE es <b>físico</b> (no soft-delete).
/// </summary>
public interface IDocumentOrderOverrideRepository
{
    /// <summary>Crea el override y devuelve el read model enriquecido (AC1/AC2).</summary>
    Task<DocumentOrderOverrideItem> CreateAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        string scopeType,
        Guid scopeRefId,
        short orden,
        Guid? createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista los overrides de una combinación (trámite, scope, referencia) ordenados por
    /// <c>sort_order</c> ascendente (AC5).
    /// </summary>
    Task<IReadOnlyList<DocumentOrderOverrideItem>> ListByScopeAsync(
        Guid procedureTypeId,
        string scopeType,
        Guid scopeRefId,
        CancellationToken cancellationToken = default);

    /// <summary>Devuelve el override por id (enriquecido), o null si no existe.</summary>
    Task<DocumentOrderOverrideItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Actualiza el <c>sort_order</c> de un override (reordenamiento por arrastre, HU #10198).
    /// Devuelve el read model enriquecido o null si el id no existe. La tupla única no cambia,
    /// así que no hay conflicto de unicidad posible.
    /// </summary>
    Task<DocumentOrderOverrideItem?> UpdateOrderAsync(
        Guid id,
        short orden,
        CancellationToken cancellationToken = default);

    /// <summary>Borrado físico; devuelve false si no existe (AC6).</summary>
    Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True si ya existe un override para la tupla única
    /// (trámite, documento, scope, referencia) — unicidad del override.
    /// </summary>
    Task<bool> ExistsUniqueAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        string scopeType,
        Guid scopeRefId,
        CancellationToken cancellationToken = default);
}
