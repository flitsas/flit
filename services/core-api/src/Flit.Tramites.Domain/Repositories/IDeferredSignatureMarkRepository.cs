using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Persistencia de las marcas de firma a posteriori (HU #11196). Todo tenant-scoped: el aislamiento por
/// tenant es lo que garantiza el AC5 (los trámites marcados de otra empresa gestora no entran al lote).
/// </summary>
public interface IDeferredSignatureMarkRepository
{
    /// <summary>Marca pendiente de un trámite y parte, o <c>null</c> si no la tiene.</summary>
    Task<DeferredSignatureMark?> FindPendienteAsync(
        Guid tenantId, Guid procedureInstanceId, string partyRole, CancellationToken cancellationToken = default);

    /// <summary>
    /// Todas las marcas PENDIENTES de una persona (por documento) en el tenant, en orden determinista de
    /// creación: es el lote que se firma cuando esa persona valida su identidad.
    /// </summary>
    Task<IReadOnlyList<DeferredSignatureMark>> ListPendientesByRepresentativeAsync(
        Guid tenantId,
        string representativeDocumentType,
        string representativeDocumentNumber,
        CancellationToken cancellationToken = default);

    void Add(DeferredSignatureMark mark);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
