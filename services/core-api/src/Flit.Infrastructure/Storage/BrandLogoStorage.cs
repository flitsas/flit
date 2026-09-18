using Flit.Admin.Application.Companies.Branding;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Adaptador del puerto <see cref="IBrandLogoStorage"/> (HU #12412 AC7): delega en
/// <see cref="IAttachmentStorage"/>, mismo patrón que <see cref="BannerImageStorage"/>
/// (ADR-0057-banners-imagen-endpoint-propio-sin-presigned). Un "grupo" por cabeza (<c>tenantId</c>),
/// así el file-manager agrupa las versiones del logotipo de cada red.
/// </summary>
internal sealed class BrandLogoStorage : IBrandLogoStorage
{
    private const string BrandLogoTipo = "brand-logo";

    private readonly IAttachmentStorage _storage;

    public BrandLogoStorage(IAttachmentStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<StoredBrandLogo> SaveAsync(
        Guid tenantId,
        string filename,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var stored = await _storage
            .SaveAsync(tenantId, BrandLogoTipo, filename, content, cancellationToken)
            .ConfigureAwait(false);

        return new StoredBrandLogo(stored.StoragePath, stored.Sha256, stored.SizeBytes);
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(storagePath)
            ? Task.FromResult<Stream?>(null)
            : _storage.OpenReadAsync(storagePath, cancellationToken);
}
