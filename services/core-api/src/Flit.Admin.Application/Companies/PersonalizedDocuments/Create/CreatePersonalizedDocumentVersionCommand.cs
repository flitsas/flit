namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Create;

/// <summary>
/// Comando de alta de una versión de documento personalizado. El handler valida el tipo y los
/// metadatos, registra el documento en storage (presigned upload) y persiste la fila en
/// <c>pendiente</c> (nunca el binario). <c>Sha256</c>/<c>SizeBytes</c> los declara el cliente; el
/// servidor los RECALCULA al confirmar.
/// </summary>
public sealed class CreatePersonalizedDocumentVersionCommand
{
    public required Guid TenantId { get; init; }
    public string? DocumentType { get; init; }
    public string? Filename { get; init; }
    public string? Sha256 { get; init; }
    public long SizeBytes { get; init; }
    public Guid? CreatedBy { get; init; }
}
