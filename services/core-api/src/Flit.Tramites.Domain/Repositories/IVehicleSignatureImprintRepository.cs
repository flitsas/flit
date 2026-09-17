using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>Persistencia de auditoría de improntas firmadas (HU #12116).</summary>
public interface IVehicleSignatureImprintRepository
{
    void Add(VehicleSignatureImprint row);

    /// <summary>
    /// Busca fila activa (<c>deleted_at IS NULL</c>) por trámite + hash del PDF base.
    /// Idempotencia al re-estampar; filas soft-deleted no bloquean una nueva firma. La unicidad
    /// (y por tanto esta búsqueda) es por <b>trámite</b>: el mismo PDF base puede firmarse en
    /// trámites distintos sin considerarse duplicado (Bug #12594).
    /// </summary>
    Task<VehicleSignatureImprint?> FindActiveByInstanceAndHashAsync(
        Guid procedureInstanceId,
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

    /// <summary>
    /// Improntas firmadas cuya instancia tiene la placa indicada (Trim+Upper).
    /// Incluye soft-deleted. Sin filtro de tenant: la firma es del documento, no del OT.
    /// </summary>
    Task<IReadOnlyList<VehicleSignatureImprintListRow>> ListByPlacaAsync(
        string placa,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene una fila por id, incluyendo soft-deleted (validación histórica).
    /// </summary>
    Task<VehicleSignatureImprint?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
