namespace Flit.Ict.Domain.Abstractions;

/// <summary>
/// Traduce un documento adjunto del catálogo ICT (<c>id_attachment</c> = allowed_documents.id) al
/// DocTipo del checklist de trámites de v2, según la MODALIDAD del trámite derivada del
/// <c>transaction_type</c> (matrícula: 1,2 · traspaso: 3,4 · otros: 5-16). El mapeo vive en
/// <c>ict.external_integration_attachment_association</c>. Si el documento no tiene equivalente en esa
/// modalidad, devuelve <c>"otro"</c> (queda registrado pero sin clasificar en el checklist).
/// </summary>
public interface IAttachmentDocTypeResolver
{
    Task<string> ResolveDocTypeAsync(int transactionType, int idAttachment, CancellationToken ct = default);
}
