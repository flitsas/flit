namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Repositorio de bloqueos de OT para cabezas Marca Blanca (HU #12407).
/// </summary>
public interface ITenantTransitOfficeBlockRepository
{
    /// <summary>Cabezas que bloquean el organismo (cálculo inverso de la lista efectiva, Bug #12912).</summary>
    Task<IReadOnlyList<Guid>> ListBlockingHeadIdsAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);

    /// <summary>Lista los ids de OT bloqueados para la cabeza.</summary>
    Task<IReadOnlyList<Guid>> ListBlockedOfficeIdsAsync(
        Guid headTenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrega un bloqueo. Idempotente: si ya existe devuelve <c>false</c> sin duplicar auditoría.
    /// </summary>
    Task<bool> AddBlockAsync(
        Guid headTenantId,
        Guid transitOfficeId,
        Guid? createdBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retira un bloqueo. Devuelve <c>false</c> si no existía (→ 404).
    /// </summary>
    Task<bool> RemoveBlockAsync(
        Guid headTenantId,
        Guid transitOfficeId,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default);
}
