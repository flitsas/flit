namespace Flit.Tramites.Application.Storage;

/// <summary>
/// Resultado de persistir un archivo: ruta relativa de almacenamiento (para borrar/servir)
/// y el SHA-256 del contenido en hex minúsculas.
/// </summary>
public sealed record StoredFile(string StoragePath, string Sha256, long SizeBytes);

/// <summary>
/// Abstracción de almacenamiento de adjuntos (FS-agnóstico para testear sin tocar disco).
/// La implementación concreta vive en Infrastructure (<c>DiskAttachmentStorage</c>).
/// </summary>
public interface IAttachmentStorage
{
    /// <summary>
    /// Persiste el contenido del adjunto y devuelve su ruta de almacenamiento + hash.
    /// El nombre final se sanea como <c>{tipo}_{timestamp}_{safeName}</c> dentro del
    /// directorio de la instancia. La implementación calcula el SHA-256 del contenido.
    /// </summary>
    Task<StoredFile> SaveAsync(
        Guid procedureInstanceId,
        string tipo,
        string originalFilename,
        Stream content,
        CancellationToken ct = default);

    /// <summary>Borra el archivo apuntado por <paramref name="storagePath"/> (no-op si no existe).</summary>
    void Delete(string storagePath);
}
