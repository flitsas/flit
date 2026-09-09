namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Persistencia de <c>admin.standalone_document_batches</c> (Feature #12201, I3).
///
/// <para>Las consultas del usuario filtran SIEMPRE por <c>tenant_id</c>. La única excepción es
/// <see cref="ClaimNextAsync"/>: el worker es un <c>BackgroundService</c> de plataforma que toma
/// lotes de TODOS los tenants —de ahí el índice parcial <c>ix_..._pendientes</c> sin
/// <c>tenant_id</c> al frente—, y el tenant se lee de la propia fila reclamada.</para>
/// </summary>
public interface IStandaloneDocumentBatchRepository
{
    /// <summary>
    /// Resuelve el replay de idempotencia del lote (CF-16). Se invoca ANTES de escribir el XLSX en
    /// storage: repetir la clave no puede dejar un archivo fuente huérfano.
    /// </summary>
    Task<StandaloneDocumentBatch?> FindByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Un lote del tenant por id. <c>null</c> si no existe o es de otra compañía.</summary>
    Task<StandaloneDocumentBatch?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Inserta la cabecera del lote en <c>queued</c>.</summary>
    Task InsertAsync(StandaloneDocumentBatch batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclama el siguiente lote procesable y lo deja en <c>processing</c> con <c>claimed_at</c>
    /// fijado. Devuelve <c>null</c> si no hay nada que hacer.
    ///
    /// <para><b>Es también el reaper (R5)</b>: además de los <c>queued</c>, vuelve reclamable todo
    /// lote en <c>processing</c> cuyo <c>claimed_at</c> sea anterior a
    /// <paramref name="reclaimBefore"/>. Que un lote atascado se reprocese es seguro porque el
    /// índice único <c>uq_standalone_documents_batch_row</c> impide generar dos veces la misma fila.</para>
    /// </summary>
    Task<StandaloneDocumentBatch?> ClaimNextAsync(
        DateTimeOffset now,
        DateTimeOffset reclaimBefore,
        CancellationToken cancellationToken = default);

    /// <summary>Cierra el lote con su estado terminal, los contadores y <c>completed_at</c>.</summary>
    Task CompleteAsync(
        Guid id,
        string status,
        int generatedCount,
        int errorCount,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}
