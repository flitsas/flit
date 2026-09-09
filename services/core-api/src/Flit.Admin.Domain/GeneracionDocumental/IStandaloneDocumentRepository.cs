namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Persistencia de <c>admin.standalone_documents</c> (Feature #12201). <b>Todos</b> los métodos
/// filtran por <c>tenant_id</c> y por <c>deleted_at IS NULL</c>: el aislamiento entre compañías es
/// este filtro más el ownership check del endpoint, nunca la política RLS (§5.4 del diseño).
/// <para>La máquina de estados se recorre con updates acotados
/// (<see cref="SaveRuesSnapshotAsync"/> → <see cref="MarkGeneratedAsync"/> |
/// <see cref="MarkErrorAsync"/>) porque el trigger <c>tr_standalone_documents_immutable</c> congela
/// snapshot y binario: un update que reescriba columnas ya escritas es rechazado por la BD.</para>
/// </summary>
public interface IStandaloneDocumentRepository
{
    /// <summary>
    /// Resuelve el replay de idempotencia (CF-16). Se invoca ANTES de consultar al proveedor: es lo
    /// que evita gastar una consulta y escribir un archivo nuevo por una petición repetida.
    /// </summary>
    Task<StandaloneDocument?> FindByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Historial paginado del tenant del filtro (CF-17/CF-18). Devuelve una PROYECCIÓN pobre en
    /// PII: sin <c>document_snapshot</c>, sin <c>rues_snapshot</c> y sin <c>storage_path</c>.
    /// </summary>
    Task<StandaloneDocumentPage> ListAsync(
        StandaloneDocumentFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Audita una descarga (CF-19) en un ÚNICO UPDATE atómico: <c>download_count</c> se incrementa
    /// con una expresión SQL sobre la propia columna y <c>downloaded_at</c> se fija en el mismo
    /// enunciado. Nunca se lee-y-escribe desde el handler: dos descargas concurrentes dejarían el
    /// contador en 1.
    /// <para>Solo afecta filas del tenant en estado <c>generated</c>. Devuelve el número de filas
    /// actualizadas (0 = no existe, es de otro tenant o no está generada).</para>
    /// <para>Las cinco columnas que toca este UPDATE están EXENTAS del trigger
    /// <c>tr_standalone_documents_immutable</c>; tocar cualquier otra aquí haría fallar la descarga
    /// con <c>check_violation</c>.</para>
    /// </summary>
    Task<int> RegisterDownloadAsync(
        Guid tenantId,
        Guid id,
        DateTimeOffset downloadedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Una fila del tenant por id. <c>null</c> si no existe o es de otro tenant.</summary>
    Task<StandaloneDocument?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Inserta la fila del intento (normalmente en <c>pending</c>).</summary>
    Task InsertAsync(StandaloneDocument document, CancellationToken cancellationToken = default);

    /// <summary>Escribe el snapshot RUES. Solo puede ejecutarse una vez por fila (trigger, capa 1).</summary>
    Task SaveRuesSnapshotAsync(
        Guid tenantId,
        Guid id,
        string ruesSnapshotJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Escribe el snapshot COMPLETO del documento de transferencia (CF-26). Igual que
    /// <see cref="SaveRuesSnapshotAsync"/>, solo puede ejecutarse una vez por fila: la capa 1 del
    /// trigger <c>tr_standalone_documents_immutable</c> congela <c>document_snapshot</c> en cuanto
    /// deja de ser nulo.
    /// </summary>
    Task SaveDocumentSnapshotAsync(
        Guid tenantId,
        Guid id,
        string documentSnapshotJson,
        CancellationToken cancellationToken = default);

    /// <summary>Cierra la fila en <c>generated</c> con el binario ya persistido en storage.</summary>
    Task MarkGeneratedAsync(
        Guid tenantId,
        Guid id,
        StandaloneDocumentFile file,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Números de fila del XLSX que YA tienen documento en el lote (Feature #12201, I3). El worker
    /// los consulta antes de procesar para no rehacer lo hecho cuando el reaper devuelve un lote
    /// atascado. Es una optimización, no el control: el control duro es el índice único
    /// <c>uq_standalone_documents_batch_row</c>.
    /// </summary>
    Task<IReadOnlyList<StandaloneDocumentBatchRowState>> ListBatchRowStatesAsync(
        Guid tenantId,
        Guid batchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Escribe <c>validation_errors</c> de una fila de lote (CF-13). Columna EXENTA del trigger de
    /// inmutabilidad, como <c>downloaded_at</c>: se puede escribir después de que el handler haya
    /// dejado la fila en <c>error</c>.
    /// </summary>
    Task SaveValidationErrorsAsync(
        Guid tenantId,
        Guid id,
        string validationErrorsJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cierra la fila en <c>error</c> con código identificable (el CHECK
    /// <c>ck_standalone_documents_error_con_codigo</c> prohíbe un error sin código).
    /// </summary>
    Task MarkErrorAsync(
        Guid tenantId,
        Guid id,
        string errorCode,
        string? errorField = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Estado de una fila ya materializada de un lote: su número en el XLSX y su resultado.</summary>
public sealed record StandaloneDocumentBatchRowState(int RowNumber, string Status);
