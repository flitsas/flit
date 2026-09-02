using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

internal sealed class IdentitySignatureArtifactStorage(IAttachmentStorage storage) : IIdentitySignatureArtifactStorage
{
    private const string ArtifactTipo = "identity_signature";
    private const string ArtifactFilename = "signature.png";

    public async Task<StoredIdentitySignature> SaveAsync(
        Guid tenantId,
        byte[] png,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length == 0)
            throw new ArgumentException("El recorte de firma no puede estar vacío.", nameof(png));

        using var content = new MemoryStream(png, writable: false);
        var stored = await storage
            .SaveAsync(tenantId, ArtifactTipo, ArtifactFilename, content, cancellationToken)
            .ConfigureAwait(false);
        return new StoredIdentitySignature(stored.StoragePath, stored.Sha256);
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return Task.FromResult<Stream?>(null);
        return storage.OpenReadAsync(storagePath.Trim(), cancellationToken);
    }
}
