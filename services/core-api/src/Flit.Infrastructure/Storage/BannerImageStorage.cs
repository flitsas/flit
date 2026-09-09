using Flit.Admin.Application.Banners.Ports;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Adaptador del puerto <see cref="IBannerImageStorage"/> (Feature #12236,
/// ADR-0057-banners-imagen-endpoint-propio-sin-presigned): delega en
/// <see cref="IAttachmentStorage"/> (file-manager / S3), igual que
/// <see cref="StandaloneDocumentStorage"/>.
///
/// <para>Solo implementa <c>OpenReadAsync</c> (HU #12240). HU #12239 (CRUD de banners, en
/// desarrollo en paralelo) anadira <c>SaveAsync</c> a la interfaz y a este adaptador —
/// conflicto de merge esperado y aceptado, ver ADR-0057.</para>
/// </summary>
internal sealed class BannerImageStorage : IBannerImageStorage
{
    private readonly IAttachmentStorage _storage;

    public BannerImageStorage(IAttachmentStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(storagePath)
            ? Task.FromResult<Stream?>(null)
            : _storage.OpenReadAsync(storagePath, cancellationToken);
    }
}
