namespace Flit.Modules.Quipux.Domain.Puertos;

/// <summary>
/// Puerto para dejar el PDF consolidado en el bucket S3 de Quipux. Quipux lee el documento de ahí:
/// el payload de radicación solo lleva la key.
/// </summary>
public interface IQuipuxDocumentUploader
{
    /// <summary>
    /// Sube el adjunto <paramref name="attachmentId"/> como <c>{prefijo}{documentName}.pdf</c> y
    /// devuelve la key resultante para <c>ContenidoDocumento</c>.
    /// </summary>
    /// <remarks>
    /// Debe llamarse UNA vez, antes de radicar, y nunca dentro de un reintento de la llamada HTTP.
    /// En FLIT 1.0 la subida vivía dentro de <c>registerDocument</c>, así que el reintento por 401
    /// re-subía con el payload ya mutado (sin <c>documentoFlit</c>), descargaba un buffer vacío y
    /// sobrescribía el PDF bueno con 0 bytes.
    /// </remarks>
    /// <exception cref="QuipuxUploadException">
    /// Si el adjunto no existe o está vacío. Falla ruidosamente a propósito: 1.0 devolvía
    /// <c>Buffer.from('')</c> y subía un PDF de 0 bytes en silencio.
    /// </exception>
    Task<string> UploadAsync(
        Guid attachmentId,
        string documentName,
        CancellationToken cancellationToken = default);
}

/// <summary>Fallo al preparar o subir el documento al bucket de Quipux.</summary>
public sealed class QuipuxUploadException : Exception
{
    public QuipuxUploadException(string message, bool isTransient)
        : base(message) => IsTransient = isTransient;

    public QuipuxUploadException(string message, bool isTransient, Exception innerException)
        : base(message, innerException) => IsTransient = isTransient;

    public bool IsTransient { get; }
}
