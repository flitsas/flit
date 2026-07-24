using Flit.Admin.Application.Companies.Deeds;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Implementación del puerto <see cref="IDeedDocumentStorage"/> (HU #10902, ADR-0033): custodia el
/// PDF de la escritura en el storage de la empresa delegando en <see cref="IAttachmentStorage"/> (S3
/// vía presigned URLs). Usa <c>CreatePresignedUploadAsync</c> para la subida directa del cliente y
/// <c>GetPresignedViewUrlAsync</c> para la visualización inline. El PDF vive en S3; en la fila de la
/// escritura solo queda el path + hash (el SHA-256 lo aporta el cliente). Aísla al módulo Admin del
/// módulo de trámites: los handlers de escrituras no referencian <see cref="IAttachmentStorage"/>.
/// </summary>
internal sealed class DeedDocumentStorage : IDeedDocumentStorage
{
    // El file-manager agrupa los objetos por un id de "instancia" en la metadata; la escritura no
    // tiene procedure_instance, así que se usa el tenant como clave de agrupación del artefacto
    // (mismo criterio que el baúl de firmas).
    private const string DocumentTipo = "escritura";
    private const string DocumentFilename = "escritura.pdf";

    private readonly IAttachmentStorage _storage;

    public DeedDocumentStorage(IAttachmentStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<DeedUploadTicket> CreateUploadAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var upload = await _storage
            .CreatePresignedUploadAsync(tenantId, DocumentTipo, DocumentFilename, cancellationToken)
            .ConfigureAwait(false);

        return new DeedUploadTicket(upload.StoragePath, upload.Url, upload.Fields);
    }

    public async Task<DeedDocumentView?> GetViewUrlAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return null;
        }

        var view = await _storage
            .GetPresignedViewUrlAsync(storagePath, cancellationToken)
            .ConfigureAwait(false);

        return view is { } v ? new DeedDocumentView(v.Url, v.ExpiresAt) : null;
    }
}
