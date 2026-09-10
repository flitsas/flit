using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Integration;

namespace Flit.Infrastructure.Storage;

/// <summary>Lee bytes de plantilla PDF de mandato vía <see cref="IAttachmentStorage"/>.</summary>
internal sealed class MandateCustomTemplateBlobReader : IMandateCustomTemplateBlobReader
{
    private readonly IAttachmentStorage _storage;

    public MandateCustomTemplateBlobReader(IAttachmentStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<byte[]?> OpenPdfAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        await using var stream = await _storage
            .OpenReadAsync(storagePath, cancellationToken)
            .ConfigureAwait(false);
        if (stream is null) return null;

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }
}
