using Flit.Admin.Application.Banners.Ports;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Adaptador del puerto <see cref="IBannerImageStorage"/> (Feature #12236, HU #12239/#12240,
/// ADR-0057-banners-imagen-endpoint-propio-sin-presigned): delega en
/// <see cref="IAttachmentStorage"/> (file-manager / S3), igual que
/// <see cref="StandaloneDocumentStorage"/>.
/// </summary>
internal sealed class BannerImageStorage : IBannerImageStorage
{
    private readonly IAttachmentStorage _storage;

    public static readonly Guid BannerStorageGroupId = Guid.Parse("00000000-0000-0000-0000-00000000ba00");

    private const string BannerImageTipo = "banner-image";

    public BannerImageStorage(IAttachmentStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<StoredBannerImage> SaveAsync(
        string filename,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var stored = await _storage
            .SaveAsync(BannerStorageGroupId, BannerImageTipo, filename, content, cancellationToken)
            .ConfigureAwait(false);

        return new StoredBannerImage(stored.StoragePath, stored.Sha256, stored.SizeBytes);
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(storagePath)
            ? Task.FromResult<Stream?>(null)
            : _storage.OpenReadAsync(storagePath, cancellationToken);
    }
}
