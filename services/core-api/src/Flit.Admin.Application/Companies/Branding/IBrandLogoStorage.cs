namespace Flit.Admin.Application.Companies.Branding;

/// <summary>Resultado de persistir el binario del logotipo: ruta opaca de almacenamiento + integridad.</summary>
public sealed record StoredBrandLogo(string StoragePath, string Sha256, long SizeBytes);

/// <summary>
/// Puerto de almacenamiento del logotipo de marca (HU #12412 AC7). Implementación en
/// <c>Flit.Infrastructure.Storage.BrandLogoStorage</c>, mismo patrón que <c>BannerImageStorage</c>
/// (ADR-0057): delega en <c>IAttachmentStorage</c>, streaming propio, sin presigned URLs.
/// </summary>
public interface IBrandLogoStorage
{
    Task<StoredBrandLogo> SaveAsync(Guid tenantId, string filename, Stream content, CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
}
