using Flit.Tramites.Application.Storage;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// Almacenamiento privado de las partes cifradas de un lote (épica #13216, HU #13372, ADR-0070 §D4). Sube desde un
/// archivo temporal en streaming, sin cargarlo en memoria: por eso no reutiliza <see cref="IAttachmentStorage.SaveAsync"/>,
/// que lo bufferiza entero en un <c>byte[]</c>.
/// </summary>
/// <example>
/// <code>
/// var stored = await storage.SubirAsync(loteId, 2, "/tmp/flit-lotes/lote-2.flz", ct);
/// // stored.StoragePath → parts.storage_path; stored.Sha256 / SizeBytes del archivo CIFRADO.
/// await using var cifrado = await storage.OpenReadAsync(stored.StoragePath, ct);
/// </code>
/// </example>
public interface IConsolidadoLoteParteStorage
{
    /// <summary>
    /// Sube el archivo cifrado de <paramref name="rutaArchivoCifrado"/>. Devuelve el id opaco del almacenamiento
    /// (<c>storage_path</c>), el SHA-256 y el tamaño del archivo cifrado.
    /// </summary>
    Task<StoredFile> SubirAsync(Guid loteId, int partNumber, string rutaArchivoCifrado, CancellationToken ct = default);

    /// <summary>El objeto cifrado como stream de solo lectura, o <c>null</c> si no existe.</summary>
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Borrado best-effort. Hoy es no-op porque el file-manager no expone borrado (V-g); el texto cifrado huérfano
    /// es ilegible una vez destruida la DEK del lote.
    /// </summary>
    void Delete(string storagePath);
}
