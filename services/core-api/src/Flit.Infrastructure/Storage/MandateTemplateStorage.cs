using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// PDF de plantilla propia de mandato por OT vía <see cref="IAttachmentStorage"/>.
/// Agrupa por <c>transitOfficeId</c> (no hay procedure_instance ni tenant en la config OT).
/// </summary>
internal sealed class MandateTemplateStorage : IMandateTemplateStorage
{
    private const string DocumentTipo = "mandato_template";

    private readonly IAttachmentStorage _storage;

    public MandateTemplateStorage(IAttachmentStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<MandateTemplateStoredFile> SavePdfAsync(
        Guid transitOfficeId,
        string fileName,
        Stream content,
        CancellationToken ct = default)
    {
        var safeName = string.IsNullOrWhiteSpace(fileName) ? "plantilla-mandato.pdf" : fileName.Trim();
        if (!safeName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            safeName += ".pdf";

        var stored = await _storage
            .SaveAsync(transitOfficeId, DocumentTipo, safeName, content, ct)
            .ConfigureAwait(false);

        return new MandateTemplateStoredFile(stored.StoragePath, stored.Sha256, stored.SizeBytes);
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
        _storage.OpenReadAsync(storagePath, ct);

    public void Delete(string? storagePath)
    {
        if (!string.IsNullOrWhiteSpace(storagePath))
            _storage.Delete(storagePath);
    }
}
