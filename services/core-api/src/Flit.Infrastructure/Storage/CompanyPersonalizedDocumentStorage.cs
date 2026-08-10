using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Implementación del puerto <see cref="ICompanyPersonalizedDocumentStorage"/> (HU #11313, ADR-0042):
/// custodia el PDF personalizado en el storage de la empresa delegando en
/// <see cref="IAttachmentStorage"/> (S3 vía presigned URLs) — mismo patrón que
/// <c>DeedDocumentStorage</c>. El artefacto se agrupa por <c>tenantId</c> (no hay procedure_instance);
/// el nombre de archivo distingue <c>mandato.pdf</c> / <c>tramite_virtual.pdf</c>.
/// </summary>
internal sealed class CompanyPersonalizedDocumentStorage : ICompanyPersonalizedDocumentStorage
{
    private const string DocumentTipo = "documento_personalizado";

    private readonly IAttachmentStorage _storage;

    public CompanyPersonalizedDocumentStorage(IAttachmentStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<PersonalizedDocumentUploadTicket> CreateUploadAsync(
        Guid tenantId,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);

        var filename = $"{documentType}.pdf";
        var upload = await _storage
            .CreatePresignedUploadAsync(tenantId, DocumentTipo, filename, cancellationToken)
            .ConfigureAwait(false);

        return new PersonalizedDocumentUploadTicket(upload.StoragePath, upload.Url, upload.Fields);
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        return _storage.OpenReadAsync(storagePath, cancellationToken);
    }
}
