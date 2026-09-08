using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>Persistencia de auditoría de improntas firmadas (HU #12116).</summary>
public interface IVehicleSignatureImprintRepository
{
    void Add(VehicleSignatureImprint row);

    /// <summary>
    /// Busca fila activa (<c>deleted_at IS NULL</c>) por hash del PDF base.
    /// Idempotencia al re-estampar; filas soft-deleted no bloquean una nueva firma.
    /// </summary>
    Task<VehicleSignatureImprint?> FindByDocumentHashAsync(
        string documentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Ids de adjunto con firma digital vigente (no soft-deleted) en la instancia.</summary>
    Task<IReadOnlySet<Guid>> ListSignedAttachmentIdsForInstanceAsync(
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete de filas ligadas a adjuntos que se reemplazan/borran: marca
    /// <c>deleted_at</c>, anula <c>attachment_id</c> y devuelve los
    /// <c>signed_storage_path</c> que NO deben borrarse del storage (snapshot).
    /// </summary>
    IReadOnlySet<string> SoftDeleteByAttachmentIds(
        IEnumerable<Guid> attachmentIds,
        DateTimeOffset deletedAt);
}
