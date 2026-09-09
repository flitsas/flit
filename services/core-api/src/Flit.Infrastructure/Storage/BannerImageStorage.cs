using Flit.Admin.Application.Banners.Ports;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

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
}
