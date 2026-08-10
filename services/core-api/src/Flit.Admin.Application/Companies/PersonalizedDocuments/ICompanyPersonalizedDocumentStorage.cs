namespace Flit.Admin.Application.Companies.PersonalizedDocuments;

/// <summary>
/// Presigned POST policy para subir el PDF personalizado DIRECTO al storage de la empresa (sin pasar
/// por el request del API). <c>StoragePath</c> es el id opaco del backend de almacenamiento (lo que
/// FLIT guarda en <c>admin.company_personalized_documents</c>); <c>Url</c> + <c>Fields</c> son la URL
/// firmada y los campos del POST policy (van ANTES del <c>file</c> en el multipart). El SHA-256
/// declarado lo calcula el cliente; el servidor lo RECALCULA al confirmar (§7 DT-6 del plan técnico).
/// </summary>
public sealed record PersonalizedDocumentUploadTicket(
    string StoragePath,
    string Url,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// Presigned GET URL de vida corta (HU #11314, ADR-0029-preview-presigned-get-inline) para
/// previsualizar el PDF inline en el navegador sin activarlo. TTL ≈ 10 min. No loguear la URL
/// completa (lleva firma HMAC).
/// </summary>
public sealed record PersonalizedDocumentView(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Puerto acotado para custodiar el PDF personalizado de una compañía (HU #11313, ADR-0042), espejo de
/// <c>IDeedDocumentStorage</c> (ADR-0033). La implementación (Infrastructure) delega en
/// <c>IAttachmentStorage</c> (S3 vía presigned URLs), agrupando por <c>tenantId</c> igual que el baúl y
/// las escrituras. Aísla al módulo Admin del módulo de trámites.
/// </summary>
public interface ICompanyPersonalizedDocumentStorage
{
    /// <summary>
    /// Registra el documento en storage y devuelve la presigned POST policy para que el cliente suba
    /// el PDF DIRECTO a S3. El binario aún no existe: el caller persiste la fila en <c>pendiente</c> y
    /// devuelve la policy para que el cliente complete la subida.
    /// </summary>
    Task<PersonalizedDocumentUploadTicket> CreateUploadAsync(
        Guid tenantId,
        string documentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre el binario subido para validarlo y recalcular su SHA-256 en el <c>confirm</c> (única
    /// oportunidad de ver los bytes). <c>null</c> si el objeto no existe en storage.
    /// </summary>
    Task<Stream?> OpenReadAsync(
        string storagePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Presigned GET URL con <c>Content-Disposition: inline</c> para previsualizar el PDF sin
    /// activarlo (HU #11314, AC3). <c>null</c> si el binario no existe o el backend no soporta
    /// presigned view.
    /// </summary>
    Task<PersonalizedDocumentView?> GetViewUrlAsync(
        string storagePath,
        CancellationToken cancellationToken = default);
}
