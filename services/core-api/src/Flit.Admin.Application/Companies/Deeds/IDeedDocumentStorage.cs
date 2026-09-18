namespace Flit.Admin.Application.Companies.Deeds;

/// <summary>
/// Ticket de subida directa al storage (ADR-0057). <c>Method</c> = <c>POST</c> (multipart con
/// <c>Fields</c>) o <c>PUT</c> (bytes crudos; Contabo). Ausente/vacío ⇒ POST.
/// </summary>
public sealed record DeedUploadTicket(
    string StoragePath,
    string Url,
    IReadOnlyDictionary<string, string> Fields,
    string Method = "POST");

/// <summary>
/// Presigned GET URL de vida corta para visualizar el PDF inline en el navegador (TTL ≈ 10 min). No
/// loguear la URL completa (contiene firma HMAC).
/// </summary>
public sealed record DeedDocumentView(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Puerto acotado para custodiar el PDF de una escritura (HU #10902, ADR-0033). La implementación
/// (Infrastructure) delega en <c>IAttachmentStorage</c> (S3 vía presigned URLs). Aísla al módulo
/// Admin del módulo de trámites: los handlers de escrituras no referencian <c>IAttachmentStorage</c>
/// directamente, solo este puerto. El PDF vive en storage; en la fila de la escritura solo queda
/// <c>StoragePath</c> + <c>StorageSha256</c>.
/// </summary>
public interface IDeedDocumentStorage
{
    /// <summary>
    /// Registra el documento en el storage y devuelve la presigned POST policy para que el cliente
    /// suba el PDF DIRECTO a S3. El binario aún no existe: el caller persiste la metadata (incl. el
    /// SHA-256 calculado por el cliente) y devuelve la policy para que el cliente complete la subida.
    /// </summary>
    Task<DeedUploadTicket> CreateUploadAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Presigned GET URL con <c>Content-Disposition: inline</c> para ver el PDF en el navegador.
    /// <c>null</c> si el binario no existe o el backend no soporta presigned view.
    /// </summary>
    Task<DeedDocumentView?> GetViewUrlAsync(
        string storagePath,
        CancellationToken cancellationToken = default);
}
