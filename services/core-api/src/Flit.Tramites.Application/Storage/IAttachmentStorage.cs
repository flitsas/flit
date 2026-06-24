namespace Flit.Tramites.Application.Storage;

/// <summary>
/// Resultado de persistir un archivo: ruta relativa de almacenamiento (para borrar/servir)
/// y el SHA-256 del contenido en hex minúsculas.
/// </summary>
public sealed record StoredFile(string StoragePath, string Sha256, long SizeBytes);

/// <summary>
/// Abstracción de almacenamiento de adjuntos (storage-agnóstica para testear sin tocar red).
/// La implementación concreta vive en Infrastructure (<c>FileManagerAttachmentStorage</c>, sobre
/// el file-manager de la empresa / S3 vía presigned URLs).
/// </summary>
public interface IAttachmentStorage
{
    /// <summary>
    /// Persiste el contenido del adjunto y devuelve su identificador de almacenamiento + hash.
    /// La implementación calcula el SHA-256 del contenido. El <c>StoragePath</c> devuelto es opaco
    /// (p. ej. el id del file-manager) y es lo que se guarda en <c>procedure_instance_attachments</c>.
    /// </summary>
    Task<StoredFile> SaveAsync(
        Guid procedureInstanceId,
        string tipo,
        string originalFilename,
        Stream content,
        CancellationToken ct = default);

    /// <summary>
    /// Borra el binario apuntado por <paramref name="storagePath"/>. Puede ser no-op si el backend
    /// de almacenamiento no expone borrado (el objeto se recupera por su ciclo de vida); flit borra
    /// la fila en su BD de todas formas.
    /// </summary>
    void Delete(string storagePath);

    /// <summary>
    /// Abre el contenido del archivo apuntado por <paramref name="storagePath"/> para lectura/streaming.
    /// Devuelve <c>null</c> si el archivo no existe (el adjunto está en BD pero el binario se perdió).
    /// </summary>
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default);
}
